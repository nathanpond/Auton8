using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;
using RecordFieldChangeEntity = AutoNate.Web.Persistence.Scaffolded.RecordFieldChange;
using RecordModel = AutoNate.Web.Models.Records.Record;
using RecordTypeFieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeField;
using System.Globalization;

namespace AutoNate.Web.Services.Records;

// Hoisted single-element ChangedFields arrays used in record-event payloads
// for the well-known mutation flavors (status flip, assignee swap, archive
// toggle). Avoids allocating a fresh string[] per write — these fire on
// every record mutation.
file static class ChangedFieldsConstants
{
    public static readonly string[] Status = ["status"];
    public static readonly string[] AssigneeIds = ["assigneeIds"];
    public static readonly string[] IsArchived = ["isArchived"];
}

public sealed class EfCoreRecordStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFieldTypeRegistry fieldTypeRegistry,
    IEntityEdgeWriter entityEdgeWriter,
    IAuthorizer authorizer,
    IRecordEventPublisher eventPublisher,
    INotificationStore notificationStore,
    ILogger<EfCoreRecordStore> logger,
    IOptions<DaprOptions> daprOptions) : IRecordStore
{
    private readonly string _sourceAppId = string.IsNullOrWhiteSpace(daprOptions.Value.AppId)
        ? "autonate.web"
        : daprOptions.Value.AppId;

    private async Task EmitAssignmentNotificationsAsync(
        Guid recordId,
        string recordKey,
        string recordName,
        IEnumerable<Guid> newlyAssignedUserIds,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        foreach (var userId in newlyAssignedUserIds)
        {
            // Don't notify the actor when they assigned the record to themselves.
            if (userId == actorId) continue;
            try
            {
                await notificationStore.CreateAsync(new CreateNotificationInput(
                    UserId: userId,
                    Kind: NotificationKinds.RecordAssigned,
                    Title: "Record assigned to you",
                    Body: $"{recordKey} — {recordName}",
                    RelatedEntityKind: NotificationEntityKinds.Record,
                    RelatedEntityId: recordId.ToString(),
                    LinkPath: $"/record/{recordKey}"),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Notification fan-out is best-effort; the record write already
                // committed and is the source of truth for assignment.
                logger.LogWarning(ex,
                    "Failed to create record-assignment notification for user {UserId} on record {RecordId}.",
                    userId, recordId);
            }
        }
    }

    public async Task<Record?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Records.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<Record?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Records.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Key == key, cancellationToken);
        return entity?.ToModel();
    }

    public Task<RecordListPage> SearchAsync(RecordSearchInput input, CancellationToken cancellationToken = default) =>
        SearchInternalAsync(input, actor: null, cancellationToken);

    public Task<RecordListPage> SearchAsync(
        RecordSearchInput input,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default) =>
        SearchInternalAsync(input, actor, cancellationToken);

    private async Task<RecordListPage> SearchInternalAsync(
        RecordSearchInput input,
        ClaimsPrincipal? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var page = Math.Max(0, input.Page);
        // 1000 is the SPA's auto-mode client/server cutoff — letting the search
        // endpoint return up to that many rows lets the records table preload
        // the full set when totalCount is small enough for in-memory handling.
        var pageSize = Math.Clamp(input.PageSize, 1, 1000);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var fields = await dbContext.RecordTypeFields.AsNoTracking()
            .Where(f => f.RecordTypeId == input.RecordTypeId)
            .ToListAsync(cancellationToken);
        var fieldModels = fields.Select(f => f.ToModel()).ToList();

        var compiler = new RecordFilterCompiler(fieldTypeRegistry, fieldModels);
        var parameters = new List<object?>();

        // Anchor parameters: 0 = record_type_id, 1 = include_archived,
        // optional 2 = assignee_id (added below).
        parameters.Add(input.RecordTypeId);
        parameters.Add(input.IncludeArchived);

        var where = new StringBuilder();
        where.Append("record_type_id = {0}::uuid AND (is_archived = FALSE OR {1} = TRUE)");

        if (input.AssigneeId is { } assigneeId)
        {
            parameters.Add(assigneeId);
            where.Append(" AND ").Append('{').Append(parameters.Count - 1).Append("}::uuid = ANY(assignee_ids)");
        }

        var filterClauses = input.Filters ?? Array.Empty<RecordFilterClause>();
        var (filterSql, filterParams) = compiler.Compile(filterClauses, parameterOffset: parameters.Count);
        if (filterSql is not null)
        {
            where.Append(" AND ").Append(filterSql);
            parameters.AddRange(filterParams);
        }

        // Free-text search across key/name/status. Backed by ILIKE so it's
        // case-insensitive contains; the SPA debounces input before hitting us.
        var searchTerm = input.Search?.Trim();
        if (!string.IsNullOrEmpty(searchTerm))
        {
            var pattern = "%" + EscapeLikePattern(searchTerm) + "%";
            parameters.Add(pattern);
            var idx = parameters.Count - 1;
            where.Append(" AND (key ILIKE {").Append(idx).Append('}')
                 .Append(" OR name ILIKE {").Append(idx).Append('}')
                 .Append(" OR status ILIKE {").Append(idx).Append("})");
        }

        // Authorization gate: append the actor's record-visibility SQL when
        // the caller hands us a ClaimsPrincipal. Tests that don't care
        // continue calling the no-actor overload and skip this entirely.
        if (actor is not null)
        {
            var visibility = await authorizer.BuildRecordSqlFilterAsync(
                actor, AutoNate.Web.Authorization.Actions.View, parameters.Count, cancellationToken);
            if (!visibility.AccessOpen)
            {
                where.Append(" AND ").Append(visibility.Sql);
                parameters.AddRange(visibility.Parameters);
            }
        }

        var orderBy = ResolveOrderByWithFields(input.Sort, fieldModels);

        var countSql = $"SELECT COUNT(*) AS \"Value\" FROM records WHERE {where}";
        var totalCount = await dbContext.Database
            .SqlQueryRaw<long>(countSql, parameters.Select(p => p!).ToArray())
            .SingleAsync(cancellationToken);

        var pageSqlBuilder = new StringBuilder();
        pageSqlBuilder.Append("SELECT ");
        pageSqlBuilder.Append("id AS \"Id\", ");
        pageSqlBuilder.Append("record_type_id AS \"RecordTypeId\", ");
        pageSqlBuilder.Append("key AS \"Key\", ");
        pageSqlBuilder.Append("key_number AS \"KeyNumber\", ");
        pageSqlBuilder.Append("name AS \"Name\", ");
        pageSqlBuilder.Append("assignee_ids AS \"AssigneeIds\", ");
        pageSqlBuilder.Append("status AS \"Status\", ");
        pageSqlBuilder.Append("due_date AS \"DueDate\", ");
        pageSqlBuilder.Append("values::text AS \"Values\", ");
        pageSqlBuilder.Append("is_archived AS \"IsArchived\", ");
        pageSqlBuilder.Append("created_at_utc AS \"CreatedAtUtc\", ");
        pageSqlBuilder.Append("created_by AS \"CreatedBy\", ");
        pageSqlBuilder.Append("updated_at_utc AS \"UpdatedAtUtc\", ");
        pageSqlBuilder.Append("updated_by AS \"UpdatedBy\" ");
        pageSqlBuilder.Append("FROM records WHERE ").Append(where);
        pageSqlBuilder.Append(" ORDER BY ").Append(orderBy);
        pageSqlBuilder.Append(" LIMIT ").Append(pageSize).Append(" OFFSET ").Append(page * pageSize);

        var rows = await dbContext.Database
            .SqlQueryRaw<RecordRow>(pageSqlBuilder.ToString(), parameters.Select(p => p!).ToArray())
            .ToListAsync(cancellationToken);

        var records = rows.Select(r => r.ToModel()).ToList();

        return new RecordListPage(records, (int)totalCount, page, pageSize);
    }

    public Task<RecordListPage> SearchAssignedAsync(
        Guid assigneeId,
        int page,
        int pageSize,
        bool includeArchived,
        string? sort,
        CancellationToken cancellationToken = default) =>
        SearchAssignedInternalAsync(assigneeId, page, pageSize, includeArchived, sort,
            actor: null, cancellationToken);

    public Task<RecordListPage> SearchAssignedAsync(
        Guid assigneeId,
        int page,
        int pageSize,
        bool includeArchived,
        string? sort,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default) =>
        SearchAssignedInternalAsync(assigneeId, page, pageSize, includeArchived, sort,
            actor, cancellationToken);

    private async Task<RecordListPage> SearchAssignedInternalAsync(
        Guid assigneeId,
        int page,
        int pageSize,
        bool includeArchived,
        string? sort,
        ClaimsPrincipal? actor,
        CancellationToken cancellationToken)
    {
        var safePage = Math.Max(0, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var parameters = new List<object?> { assigneeId, includeArchived };
        var where = new StringBuilder("{0}::uuid = ANY(assignee_ids) AND (is_archived = FALSE OR {1} = TRUE)");

        if (actor is not null)
        {
            var visibility = await authorizer.BuildRecordSqlFilterAsync(
                actor, AutoNate.Web.Authorization.Actions.View, parameters.Count, cancellationToken);
            if (!visibility.AccessOpen)
            {
                where.Append(" AND ").Append(visibility.Sql);
                parameters.AddRange(visibility.Parameters);
            }
        }

        var orderBy = ResolveOrderBy(sort);

        var paramArray = parameters.Select(p => p!).ToArray();
        var countSql = $"SELECT COUNT(*) AS \"Value\" FROM records WHERE {where}";
        var totalCount = await dbContext.Database
            .SqlQueryRaw<long>(countSql, paramArray)
            .SingleAsync(cancellationToken);

        var pageSql = new StringBuilder();
        pageSql.Append("SELECT ");
        pageSql.Append("id AS \"Id\", ");
        pageSql.Append("record_type_id AS \"RecordTypeId\", ");
        pageSql.Append("key AS \"Key\", ");
        pageSql.Append("key_number AS \"KeyNumber\", ");
        pageSql.Append("name AS \"Name\", ");
        pageSql.Append("assignee_ids AS \"AssigneeIds\", ");
        pageSql.Append("status AS \"Status\", ");
        pageSql.Append("due_date AS \"DueDate\", ");
        pageSql.Append("values::text AS \"Values\", ");
        pageSql.Append("is_archived AS \"IsArchived\", ");
        pageSql.Append("created_at_utc AS \"CreatedAtUtc\", ");
        pageSql.Append("created_by AS \"CreatedBy\", ");
        pageSql.Append("updated_at_utc AS \"UpdatedAtUtc\", ");
        pageSql.Append("updated_by AS \"UpdatedBy\" ");
        pageSql.Append("FROM records WHERE ").Append(where);
        pageSql.Append(" ORDER BY ").Append(orderBy);
        pageSql.Append(" LIMIT ").Append(safePageSize).Append(" OFFSET ").Append(safePage * safePageSize);

        var rows = await dbContext.Database
            .SqlQueryRaw<RecordRow>(pageSql.ToString(), paramArray)
            .ToListAsync(cancellationToken);

        var records = rows.Select(r => r.ToModel()).ToList();
        return new RecordListPage(records, (int)totalCount, safePage, safePageSize);
    }

    public async Task<RecordListPage> ListAuthorizedAsync(
        ClaimsPrincipal actor,
        Guid? recordTypeId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var safePage = Math.Max(0, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<RecordEntity> baseQuery = dbContext.Records.AsNoTracking();
        if (recordTypeId is { } typeId)
        {
            baseQuery = baseQuery.Where(r => r.RecordTypeId == typeId);
        }

        if (!includeArchived)
        {
            baseQuery = baseQuery.Where(r => !r.IsArchived);
        }

        var visible = await authorizer.FilterQueryAsync(
            dbContext,
            actor,
            EntityKinds.Record,
            Actions.View,
            baseQuery,
            cancellationToken);

        var total = await visible.CountAsync(cancellationToken);
        var rows = await visible
            .OrderByDescending(r => r.UpdatedAtUtc)
            .Skip(safePage * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var models = rows.Select(r => r.ToModel()).ToList();
        return new RecordListPage(models, total, safePage, safePageSize);
    }

    public async Task<Record> CreateAsync(CreateRecordInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new RecordValidationException("name is required.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var typeEntity = await dbContext.RecordTypes
            .SingleOrDefaultAsync(t => t.Id == input.RecordTypeId, cancellationToken)
            ?? throw new RecordValidationException($"Record type '{input.RecordTypeId}' was not found.");

        if (typeEntity.IsArchived)
        {
            throw new RecordValidationException("Cannot create records in an archived record type.");
        }

        var fields = await dbContext.RecordTypeFields.AsNoTracking()
            .Where(f => f.RecordTypeId == input.RecordTypeId)
            .ToListAsync(cancellationToken);

        var validation = ValidateValuesPayload(fields, input.Values, isCreate: true);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Atomic key allocation: row-level lock on record_types row.
        // RETURNING (next_key_number - 1) gives us the value we just consumed.
        // EF Core's SqlQueryRaw can't compose UPDATE/RETURNING, so go through
        // ADO.NET directly. The command joins the EF transaction so it commits
        // atomically with the record insert + history rows.
        long allocatedKeyNumber = await AllocateKeyNumberAsync(
            dbContext, input.RecordTypeId, actorId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var recordId = Guid.NewGuid();
        var changeSetId = Guid.NewGuid();
        var key = $"{typeEntity.ShortCode}-{allocatedKeyNumber}";
        var assigneeIds = (input.AssigneeIds ?? Array.Empty<Guid>()).Distinct().ToArray();
        var valuesJson = validation.NormalizedValues.GetRawText();

        var status = string.IsNullOrWhiteSpace(input.Status) ? null : input.Status.Trim();
        var entity = new RecordEntity
        {
            Id = recordId,
            RecordTypeId = input.RecordTypeId,
            Key = key,
            KeyNumber = allocatedKeyNumber,
            Name = name,
            AssigneeIds = assigneeIds,
            Status = status,
            DueDate = input.DueDate,
            Values = valuesJson,
            IsArchived = false,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId,
            UpdatedAtUtc = now.UtcDateTime,
            UpdatedBy = actorId
        };
        dbContext.Records.Add(entity);

        // History: one 'created' row + one 'value_changed' row per non-empty field
        // so that "history of field X" queries naturally include the initial value.
        // All rows from this transaction share a single change_set_id so the UI
        // can render them as one timeline entry.
        dbContext.RecordFieldChanges.Add(BuildHistory(
            recordId, changeSetId, RecordChangeKinds.Created, fieldKey: null,
            oldValue: null,
            newValue: SerializeCreatedSnapshot(name, assigneeIds, status, input.DueDate, validation.NormalizedValues),
            actorId, now));

        foreach (var field in fields)
        {
            if (field.IsArchived) continue;
            if (validation.NormalizedValues.TryGetProperty(field.FieldKey, out var prop) &&
                prop.ValueKind != JsonValueKind.Null)
            {
                dbContext.RecordFieldChanges.Add(BuildHistory(
                    recordId, changeSetId, RecordChangeKinds.ValueChanged, field.FieldKey,
                    oldValue: null,
                    newValue: prop.GetRawText(),
                    actorId, now));
            }
        }

        // Phase 2: shadow Record.CreatedBy and Record.AssigneeIds into entity_edges
        // so the new authorization model has a uniform truth source. Reads still
        // come from the legacy columns; this is a dual-write only.
        var recordIdString = recordId.ToString();
        entityEdgeWriter.AddEdge(
            dbContext, EdgeKinds.Creator,
            EntityKinds.User, actorId.ToString(),
            EntityKinds.Record, recordIdString,
            actorId, now);

        foreach (var assigneeId in assigneeIds)
        {
            entityEdgeWriter.AddEdge(
                dbContext, EdgeKinds.Assignee,
                EntityKinds.User, assigneeId.ToString(),
                EntityKinds.Record, recordIdString,
                actorId, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await eventPublisher.PublishAsync(new RecordEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: RecordEventTypes.Created,
            OccurredAtUtc: now,
            RecordId: entity.Id,
            Key: entity.Key,
            RecordTypeId: entity.RecordTypeId,
            Name: entity.Name,
            Status: entity.Status,
            PreviousStatus: null,
            ChangedFields: Array.Empty<string>(),
            AssigneeIds: assigneeIds,
            IsArchived: entity.IsArchived,
            ActorId: actorId,
            SourceAppId: _sourceAppId), cancellationToken);

        await EmitAssignmentNotificationsAsync(
            entity.Id, entity.Key, entity.Name, assigneeIds, actorId, cancellationToken);

        return entity.ToModel();
    }

    public async Task<Record> UpdateAsync(Guid id, UpdateRecordInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Records.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new RecordNotFoundException(id);

        var fields = await dbContext.RecordTypeFields.AsNoTracking()
            .Where(f => f.RecordTypeId == entity.RecordTypeId)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        // One change_set_id per mutation so the UI can render N field changes
        // as a single timeline entry.
        var changeSetId = Guid.NewGuid();
        var historyRows = new List<RecordFieldChangeEntity>();
        var changedFields = new List<string>();
        var statusChanged = false;
        var previousStatus = entity.Status;

        if (input.Name is { } newNameRaw)
        {
            var newName = newNameRaw.Trim();
            if (newName.Length == 0)
            {
                throw new RecordValidationException("name cannot be empty.");
            }
            if (!string.Equals(entity.Name, newName, StringComparison.Ordinal))
            {
                historyRows.Add(BuildHistory(
                    entity.Id, changeSetId, RecordChangeKinds.NameChanged, fieldKey: null,
                    oldValue: JsonSerializer.Serialize(entity.Name),
                    newValue: JsonSerializer.Serialize(newName),
                    actorId, now));
                entity.Name = newName;
                changedFields.Add("name");
            }
        }

        Guid[]? assigneeEdgeOld = null;
        Guid[]? assigneeEdgeNew = null;
        if (input.AssigneeIds is { } incomingAssignees)
        {
            var newAssignees = incomingAssignees.Distinct().ToArray();
            if (!entity.AssigneeIds.SequenceEqual(newAssignees))
            {
                historyRows.Add(BuildHistory(
                    entity.Id, changeSetId, RecordChangeKinds.AssigneesChanged, fieldKey: null,
                    oldValue: JsonSerializer.Serialize(entity.AssigneeIds),
                    newValue: JsonSerializer.Serialize(newAssignees),
                    actorId, now));
                assigneeEdgeOld = entity.AssigneeIds;
                assigneeEdgeNew = newAssignees;
                entity.AssigneeIds = newAssignees;
                changedFields.Add("assigneeIds");
            }
        }

        if (input.Status.HasValue)
        {
            var raw = input.Status.Value;
            var newStatus = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (!string.Equals(entity.Status, newStatus, StringComparison.Ordinal))
            {
                historyRows.Add(BuildHistory(
                    entity.Id, changeSetId, RecordChangeKinds.StatusChanged, fieldKey: null,
                    oldValue: JsonSerializer.Serialize(entity.Status),
                    newValue: JsonSerializer.Serialize(newStatus),
                    actorId, now));
                entity.Status = newStatus;
                changedFields.Add("status");
                statusChanged = true;
            }
        }

        if (input.DueDate.HasValue)
        {
            var newDueDate = input.DueDate.Value;
            if (entity.DueDate != newDueDate)
            {
                historyRows.Add(BuildHistory(
                    entity.Id, changeSetId, RecordChangeKinds.DueDateChanged, fieldKey: null,
                    oldValue: JsonSerializer.Serialize(entity.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    newValue: JsonSerializer.Serialize(newDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    actorId, now));
                entity.DueDate = newDueDate;
                changedFields.Add("dueDate");
            }
        }

        if (input.Values is { } incomingValues && incomingValues.ValueKind != JsonValueKind.Undefined)
        {
            if (incomingValues.ValueKind != JsonValueKind.Object)
            {
                throw new RecordValidationException("values must be a JSON object.");
            }

            var existingValues = RecordPersistenceMapper.ParseJson(entity.Values);
            var validation = ValidatePartialValuesPayload(fields, incomingValues, existingValues);

            foreach (var (fieldKey, oldValue, newValue) in validation.PerFieldDiffs)
            {
                historyRows.Add(BuildHistory(
                    entity.Id, changeSetId, RecordChangeKinds.ValueChanged, fieldKey,
                    oldValue: oldValue,
                    newValue: newValue,
                    actorId, now));
                changedFields.Add($"values.{fieldKey}");
            }

            if (validation.PerFieldDiffs.Count > 0)
            {
                entity.Values = validation.MergedValues.GetRawText();
            }
        }

        if (changedFields.Count == 0)
        {
            return entity.ToModel();
        }

        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var row in historyRows)
        {
            dbContext.RecordFieldChanges.Add(row);
        }

        if (assigneeEdgeOld is not null && assigneeEdgeNew is not null)
        {
            await entityEdgeWriter.SyncUserEdgesAsync(
                dbContext,
                EdgeKinds.Assignee,
                EntityKinds.Record,
                entity.Id.ToString(),
                assigneeEdgeOld,
                assigneeEdgeNew,
                actorId,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await eventPublisher.PublishAsync(new RecordEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: RecordEventTypes.Updated,
            OccurredAtUtc: now,
            RecordId: entity.Id,
            Key: entity.Key,
            RecordTypeId: entity.RecordTypeId,
            Name: entity.Name,
            Status: entity.Status,
            PreviousStatus: statusChanged ? previousStatus : null,
            ChangedFields: changedFields,
            AssigneeIds: entity.AssigneeIds,
            IsArchived: entity.IsArchived,
            ActorId: actorId,
            SourceAppId: _sourceAppId), cancellationToken);

        if (statusChanged)
        {
            await eventPublisher.PublishAsync(new RecordEventEnvelope(
                EventId: Guid.NewGuid(),
                EventType: RecordEventTypes.StatusChanged,
                OccurredAtUtc: now,
                RecordId: entity.Id,
                Key: entity.Key,
                RecordTypeId: entity.RecordTypeId,
                Name: entity.Name,
                Status: entity.Status,
                PreviousStatus: previousStatus,
                ChangedFields: ChangedFieldsConstants.Status,
                AssigneeIds: entity.AssigneeIds,
                IsArchived: entity.IsArchived,
                ActorId: actorId,
                SourceAppId: _sourceAppId), cancellationToken);
        }

        if (changedFields.Contains("assigneeIds"))
        {
            await eventPublisher.PublishAsync(new RecordEventEnvelope(
                EventId: Guid.NewGuid(),
                EventType: RecordEventTypes.AssigneesChanged,
                OccurredAtUtc: now,
                RecordId: entity.Id,
                Key: entity.Key,
                RecordTypeId: entity.RecordTypeId,
                Name: entity.Name,
                Status: entity.Status,
                PreviousStatus: null,
                ChangedFields: ChangedFieldsConstants.AssigneeIds,
                AssigneeIds: entity.AssigneeIds,
                IsArchived: entity.IsArchived,
                ActorId: actorId,
                SourceAppId: _sourceAppId), cancellationToken);
        }

        if (assigneeEdgeOld is not null && assigneeEdgeNew is not null)
        {
            var addedAssignees = assigneeEdgeNew.Except(assigneeEdgeOld).ToArray();
            if (addedAssignees.Length > 0)
            {
                await EmitAssignmentNotificationsAsync(
                    entity.Id, entity.Key, entity.Name, addedAssignees, actorId, cancellationToken);
            }

            var removedAssignees = assigneeEdgeOld.Except(assigneeEdgeNew).ToArray();
            foreach (var userId in removedAssignees)
            {
                try
                {
                    await notificationStore.DeleteByRelatedEntityAsync(
                        userId,
                        NotificationEntityKinds.Record,
                        entity.Id.ToString(),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to clear record-assignment notifications for user {UserId} on record {RecordId}.",
                        userId, entity.Id);
                }
            }
        }

        return entity.ToModel();
    }

    public async Task<Record> DeleteAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Records.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new RecordNotFoundException(id);

        // Snapshot before delete so the event envelope carries the values that
        // existed at deletion time. CASCADE handles record_edges,
        // record_comments, record_field_changes, and record_watches.
        var snapshot = entity.ToModel();
        var now = DateTimeOffset.UtcNow;

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Records.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await eventPublisher.PublishAsync(new RecordEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: RecordEventTypes.Purged,
            OccurredAtUtc: now,
            RecordId: snapshot.Id,
            Key: snapshot.Key,
            RecordTypeId: snapshot.RecordTypeId,
            Name: snapshot.Name,
            Status: snapshot.Status,
            PreviousStatus: null,
            ChangedFields: Array.Empty<string>(),
            AssigneeIds: snapshot.AssigneeIds,
            IsArchived: snapshot.IsArchived,
            ActorId: actorId,
            SourceAppId: _sourceAppId), cancellationToken);

        return snapshot;
    }

    public async Task<Record> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Records.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new RecordNotFoundException(id);

        if (entity.IsArchived == archived)
        {
            return entity.ToModel();
        }

        var now = DateTimeOffset.UtcNow;

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        entity.IsArchived = archived;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        dbContext.RecordFieldChanges.Add(BuildHistory(
            entity.Id,
            Guid.NewGuid(),
            archived ? RecordChangeKinds.Archived : RecordChangeKinds.Unarchived,
            fieldKey: null,
            oldValue: null,
            newValue: null,
            actorId, now));

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await eventPublisher.PublishAsync(new RecordEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: archived ? RecordEventTypes.Deleted : RecordEventTypes.Restored,
            OccurredAtUtc: now,
            RecordId: entity.Id,
            Key: entity.Key,
            RecordTypeId: entity.RecordTypeId,
            Name: entity.Name,
            Status: entity.Status,
            PreviousStatus: null,
            ChangedFields: ChangedFieldsConstants.IsArchived,
            AssigneeIds: entity.AssigneeIds,
            IsArchived: entity.IsArchived,
            ActorId: actorId,
            SourceAppId: _sourceAppId), cancellationToken);

        return entity.ToModel();
    }

    private static async Task<long> AllocateKeyNumberAsync(
        AutoNateDbContext dbContext,
        Guid recordTypeId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = @"UPDATE record_types
                                SET next_key_number = next_key_number + 1,
                                    updated_at_utc = NOW(),
                                    updated_by = @actor
                                WHERE id = @id
                                RETURNING (next_key_number - 1)";

        var idParam = command.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = recordTypeId;
        command.Parameters.Add(idParam);

        var actorParam = command.CreateParameter();
        actorParam.ParameterName = "@actor";
        actorParam.Value = actorId;
        command.Parameters.Add(actorParam);

        var result = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new RecordValidationException($"Record type '{recordTypeId}' was not found.");
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static string ResolveOrderBy(string? sort) => sort switch
    {
        "key_asc" => "key_number ASC",
        "key_desc" => "key_number DESC",
        "name_asc" => "name ASC",
        "name_desc" => "name DESC",
        "status_asc" => "status ASC NULLS LAST",
        "status_desc" => "status DESC NULLS LAST",
        "due_date_asc" => "due_date ASC NULLS LAST",
        "due_date_desc" => "due_date DESC NULLS LAST",
        "created_asc" => "created_at_utc ASC",
        "created_desc" => "created_at_utc DESC",
        "updated_asc" => "updated_at_utc ASC",
        _ => "updated_at_utc DESC"
    };

    // Like ResolveOrderBy, but additionally accepts `field:<fieldKey>:asc|desc`
    // tokens that sort by a user-defined field on the record type. The fieldKey
    // is validated against the record type's field list AND a snake_case regex
    // before being interpolated into SQL. Casts numeric/boolean fields so they
    // don't sort as text ("10" before "9"). Unsupported tokens fall through to
    // the built-in resolver.
    //
    // Note: every sort scans the JSONB blob row-by-row; fine through low
    // thousands of rows but a per-field functional index would be needed for
    // larger record types.
    private static string ResolveOrderByWithFields(
        string? sort,
        IReadOnlyList<RecordTypeField> fields)
    {
        if (sort is not null && sort.StartsWith("field:", StringComparison.Ordinal))
        {
            var rest = sort.AsSpan("field:".Length);
            var sepIdx = rest.LastIndexOf(':');
            if (sepIdx > 0)
            {
                var fieldKey = rest[..sepIdx].ToString();
                var direction = rest[(sepIdx + 1)..].ToString();
                if ((direction == "asc" || direction == "desc")
                    && IsValidFieldKey(fieldKey))
                {
                    var field = fields.FirstOrDefault(f =>
                        string.Equals(f.FieldKey, fieldKey, StringComparison.Ordinal));
                    if (field is not null)
                    {
                        var dir = direction == "asc" ? "ASC" : "DESC";
                        var expr = BuildJsonExtractExpr(fieldKey, field.DataType);
                        return $"{expr} {dir} NULLS LAST";
                    }
                }
            }
        }
        return ResolveOrderBy(sort);
    }

    // Defensive: keys come from a validated table (RecordFieldKey.IsValid),
    // but this guards against any drift before string-interpolating into SQL.
    private static readonly Regex FieldKeyRegex =
        new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Compiled);
    private static bool IsValidFieldKey(string s) => FieldKeyRegex.IsMatch(s);

    // Map a field's data type to the SQL expression used for sorting. Numeric
    // and boolean values get cast so they don't sort as text; everything else
    // sorts as text (ISO 8601 dates sort lexically, which matches their
    // natural order). NULLIF empties so casts don't blow up on blank values.
    private static string BuildJsonExtractExpr(string fieldKey, string dataType) =>
        dataType switch
        {
            "number" => $"NULLIF(values->>'{fieldKey}', '')::numeric",
            "boolean" => $"NULLIF(values->>'{fieldKey}', '')::boolean",
            _ => $"values->>'{fieldKey}'"
        };

    // ILIKE wildcard escape — back-slashes the % and _ literals so a user
    // typing "50%" matches the literal string and not "50 anything".
    private static string EscapeLikePattern(string input)
    {
        var sb = new StringBuilder(input.Length + 8);
        foreach (var c in input)
        {
            if (c == '\\' || c == '%' || c == '_') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private record class CreateValidationResult(JsonElement NormalizedValues);

    private CreateValidationResult ValidateValuesPayload(
        IReadOnlyList<RecordTypeFieldEntity> fieldsRaw,
        JsonElement payload,
        bool isCreate)
    {
        if (payload.ValueKind != JsonValueKind.Object && payload.ValueKind != JsonValueKind.Undefined)
        {
            throw new RecordValidationException("values must be a JSON object.");
        }

        var fields = fieldsRaw.Select(f => f.ToModel()).ToList();
        var fieldsByKey = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);

        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in payload.EnumerateObject())
            {
                if (!fieldsByKey.ContainsKey(prop.Name))
                {
                    throw new RecordValidationException($"Unknown field '{prop.Name}'.");
                }
            }
        }

        var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<FieldValidationError>();

        foreach (var field in fields)
        {
            if (field.IsArchived) continue;
            if (!fieldTypeRegistry.TryGet(field.DataType, out var fieldType))
            {
                throw new RecordValidationException($"Unknown data type '{field.DataType}' on field '{field.FieldKey}'.");
            }

            var raw = TryGetProperty(payload, field.FieldKey, out var present)
                ? present
                : default;
            var hasValue = raw.ValueKind != JsonValueKind.Undefined;

            if (!hasValue && isCreate && field.IsRequired)
            {
                errors.Add(new FieldValidationError("required", $"Field '{field.FieldKey}' is required."));
                continue;
            }

            if (!hasValue) continue;

            var result = fieldType.ValidateValue(raw, field.Config, field.IsRequired, out var normalized);
            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                {
                    errors.Add(new FieldValidationError(err.Code, $"{field.FieldKey}: {err.Message}"));
                }
                continue;
            }

            if (normalized.ValueKind != JsonValueKind.Null)
            {
                output[field.FieldKey] = normalized.GetRawText();
            }
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException("One or more fields are invalid.", errors);
        }

        var json = "{" + string.Join(",", output.Select(kvp =>
            $"\"{JsonEscape(kvp.Key)}\":{kvp.Value}")) + "}";
        using var doc = JsonDocument.Parse(json);
        return new CreateValidationResult(doc.RootElement.Clone());
    }

    private record class PartialValidationResult(
        JsonElement MergedValues,
        IReadOnlyList<(string FieldKey, string? OldValue, string? NewValue)> PerFieldDiffs);

    private PartialValidationResult ValidatePartialValuesPayload(
        IReadOnlyList<RecordTypeFieldEntity> fieldsRaw,
        JsonElement payload,
        JsonElement existingValues)
    {
        var fields = fieldsRaw.Select(f => f.ToModel()).ToList();
        var fieldsByKey = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);
        var errors = new List<FieldValidationError>();
        var diffs = new List<(string FieldKey, string? OldValue, string? NewValue)>();

        // Start the merged map from the existing values so we keep untouched fields.
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (existingValues.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in existingValues.EnumerateObject())
            {
                merged[prop.Name] = prop.Value.GetRawText();
            }
        }

        foreach (var prop in payload.EnumerateObject())
        {
            if (!fieldsByKey.TryGetValue(prop.Name, out var field))
            {
                throw new RecordValidationException($"Unknown field '{prop.Name}'.");
            }
            if (field.IsArchived)
            {
                throw new RecordValidationException($"Field '{prop.Name}' is archived.");
            }
            if (!fieldTypeRegistry.TryGet(field.DataType, out var fieldType))
            {
                throw new RecordValidationException($"Unknown data type '{field.DataType}' on field '{field.FieldKey}'.");
            }

            var result = fieldType.ValidateValue(prop.Value, field.Config, field.IsRequired, out var normalized);
            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                {
                    errors.Add(new FieldValidationError(err.Code, $"{field.FieldKey}: {err.Message}"));
                }
                continue;
            }

            string? oldRaw = merged.TryGetValue(field.FieldKey, out var existing) ? existing : null;
            string? newRaw = normalized.ValueKind == JsonValueKind.Null ? null : normalized.GetRawText();

            if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal))
            {
                continue;
            }

            diffs.Add((field.FieldKey, oldRaw, newRaw));
            if (newRaw is null)
            {
                merged.Remove(field.FieldKey);
            }
            else
            {
                merged[field.FieldKey] = newRaw;
            }
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException("One or more fields are invalid.", errors);
        }

        var mergedJson = "{" + string.Join(",", merged.Select(kvp =>
            $"\"{JsonEscape(kvp.Key)}\":{kvp.Value}")) + "}";
        using var doc = JsonDocument.Parse(mergedJson);
        return new PartialValidationResult(doc.RootElement.Clone(), diffs);
    }

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var prop))
        {
            value = prop;
            return true;
        }
        value = default;
        return false;
    }

    private static string JsonEscape(string raw) =>
        raw.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string SerializeCreatedSnapshot(
        string name,
        Guid[] assigneeIds,
        string? status,
        DateOnly? dueDate,
        JsonElement values)
    {
        return JsonSerializer.Serialize(new
        {
            name,
            assignee_ids = assigneeIds,
            status,
            due_date = dueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            values = JsonSerializer.Deserialize<JsonElement>(values.GetRawText())
        });
    }

    private static RecordFieldChangeEntity BuildHistory(
        Guid recordId,
        Guid changeSetId,
        string changeKind,
        string? fieldKey,
        string? oldValue,
        string? newValue,
        Guid actorId,
        DateTimeOffset when) => new()
        {
            RecordId = recordId,
            ChangeSetId = changeSetId,
            ChangeKind = changeKind,
            FieldKey = fieldKey,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = actorId,
            ChangedAtUtc = when.UtcDateTime
        };

    /// <summary>
    /// SQL-projection row for the search query. Mirrors records' columns but
    /// with `values` projected as text (so EF returns it as the entity's
    /// configured string mapping).
    /// </summary>
    private sealed class RecordRow
    {
        public Guid Id { get; set; }
        public Guid RecordTypeId { get; set; }
        public string Key { get; set; } = null!;
        public long KeyNumber { get; set; }
        public string Name { get; set; } = null!;
        public Guid[] AssigneeIds { get; set; } = Array.Empty<Guid>();
        public string? Status { get; set; }
        public DateOnly? DueDate { get; set; }
        public string Values { get; set; } = "{}";
        public bool IsArchived { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public Guid UpdatedBy { get; set; }

        public RecordModel ToModel() => new()
        {
            Id = Id,
            RecordTypeId = RecordTypeId,
            Key = Key,
            KeyNumber = KeyNumber,
            Name = Name,
            AssigneeIds = AssigneeIds.ToList(),
            Status = Status,
            DueDate = DueDate,
            Values = RecordPersistenceMapper.ParseJson(Values),
            IsArchived = IsArchived,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(CreatedAtUtc),
            CreatedBy = CreatedBy,
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(UpdatedAtUtc),
            UpdatedBy = UpdatedBy
        };
    }
}
