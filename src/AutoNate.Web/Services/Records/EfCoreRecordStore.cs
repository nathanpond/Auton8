using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;
using RecordFieldChangeEntity = AutoNate.Web.Persistence.Scaffolded.RecordFieldChange;
using RecordModel = AutoNate.Web.Models.Records.Record;
using RecordTypeFieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeField;

namespace AutoNate.Web.Services.Records;

public sealed class EfCoreRecordStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFieldTypeRegistry fieldTypeRegistry,
    IEntityEdgeWriter entityEdgeWriter,
    IAuthorizer authorizer) : IRecordStore
{
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
        var pageSize = Math.Clamp(input.PageSize, 1, 200);

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
            where.Append(" AND ").Append("{").Append(parameters.Count - 1).Append("}::uuid = ANY(assignee_ids)");
        }

        var filterClauses = input.Filters ?? Array.Empty<RecordFilterClause>();
        var (filterSql, filterParams) = compiler.Compile(filterClauses, parameterOffset: parameters.Count);
        if (filterSql is not null)
        {
            where.Append(" AND ").Append(filterSql);
            parameters.AddRange(filterParams);
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

        var orderBy = ResolveOrderBy(input.Sort);

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
        var changed = false;

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
                changed = true;
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
                changed = true;
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
                changed = true;
            }
        }

        if (input.DueDate.HasValue)
        {
            var newDueDate = input.DueDate.Value;
            if (entity.DueDate != newDueDate)
            {
                historyRows.Add(BuildHistory(
                    entity.Id, changeSetId, RecordChangeKinds.DueDateChanged, fieldKey: null,
                    oldValue: JsonSerializer.Serialize(entity.DueDate?.ToString("yyyy-MM-dd")),
                    newValue: JsonSerializer.Serialize(newDueDate?.ToString("yyyy-MM-dd")),
                    actorId, now));
                entity.DueDate = newDueDate;
                changed = true;
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
                changed = true;
            }

            if (validation.PerFieldDiffs.Count > 0)
            {
                entity.Values = validation.MergedValues.GetRawText();
            }
        }

        if (!changed)
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

        return entity.ToModel();
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
        return Convert.ToInt64(result);
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
        "created_desc" => "created_at_utc DESC",
        _ => "updated_at_utc DESC"
    };

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
            due_date = dueDate?.ToString("yyyy-MM-dd"),
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
