using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;
using RecordTypeFieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeField;
using RecordTypeAuditEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeAuditEntry;

namespace AutoNate.Web.Services.Records;

public sealed class EfCoreRecordTypeStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFieldTypeRegistry fieldTypeRegistry) : IRecordTypeStore
{
    public async Task<IReadOnlyList<RecordType>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.RecordTypes.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(t => !t.IsArchived);
        }

        var types = await query
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return types.Select(t => t.ToModel()).ToList();
    }

    public async Task<RecordType?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypes.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<RecordType?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        var normalized = RecordTypeShortCode.Normalize(shortCode);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypes.AsNoTracking()
            .SingleOrDefaultAsync(t => t.ShortCode == normalized, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<RecordType> CreateAsync(CreateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var shortCode = RecordTypeShortCode.Normalize(input.ShortCode ?? string.Empty);
        if (!RecordTypeShortCode.IsValid(shortCode))
        {
            throw new RecordTypeValidationException("short_code must be 2-8 characters: start with a letter, then letters or digits.");
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new RecordTypeValidationException("name is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var model = new RecordType
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Icon = string.IsNullOrWhiteSpace(input.Icon) ? null : input.Icon.Trim(),
            Color = string.IsNullOrWhiteSpace(input.Color) ? null : input.Color.Trim(),
            IsSystem = false,
            IsArchived = false,
            NextKeyNumber = 1,
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.RecordTypes.AnyAsync(t => t.ShortCode == shortCode, cancellationToken))
        {
            throw new RecordTypeValidationException($"short_code '{shortCode}' is already in use.");
        }

        var entity = new RecordTypeEntity();
        entity.Apply(model);
        dbContext.RecordTypes.Add(entity);

        dbContext.RecordTypeAuditLog.Add(BuildAudit(
            model.Id, RecordTypeAuditChangeKinds.TypeCreated, before: null, after: SerializeTypeSnapshot(model), actorId, now));

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordType> UpdateAsync(Guid id, UpdateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new RecordTypeValidationException("name is required.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypes
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new RecordTypeNotFoundException(id);

        var before = entity.ToModel();
        var now = DateTimeOffset.UtcNow;

        entity.Name = name;
        entity.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        entity.Icon = string.IsNullOrWhiteSpace(input.Icon) ? null : input.Icon.Trim();
        entity.Color = string.IsNullOrWhiteSpace(input.Color) ? null : input.Color.Trim();
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        var after = entity.ToModel();
        dbContext.RecordTypeAuditLog.Add(BuildAudit(
            id, RecordTypeAuditChangeKinds.TypeUpdated, SerializeTypeSnapshot(before), SerializeTypeSnapshot(after), actorId, now));

        await dbContext.SaveChangesAsync(cancellationToken);
        return after;
    }

    public async Task<RecordType> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypes
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new RecordTypeNotFoundException(id);

        if (entity.IsArchived == archived)
        {
            return entity.ToModel();
        }

        var before = entity.ToModel();
        var now = DateTimeOffset.UtcNow;

        entity.IsArchived = archived;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.UpdatedBy = actorId;

        var after = entity.ToModel();
        dbContext.RecordTypeAuditLog.Add(BuildAudit(
            id,
            archived ? RecordTypeAuditChangeKinds.TypeArchived : RecordTypeAuditChangeKinds.TypeUnarchived,
            SerializeTypeSnapshot(before),
            SerializeTypeSnapshot(after),
            actorId,
            now));

        await dbContext.SaveChangesAsync(cancellationToken);
        return after;
    }

    public async Task<IReadOnlyList<RecordTypeField>> ListFieldsAsync(Guid recordTypeId, bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.RecordTypeFields.AsNoTracking()
            .Where(f => f.RecordTypeId == recordTypeId);
        if (!includeArchived)
        {
            query = query.Where(f => !f.IsArchived);
        }

        var fields = await query
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.DisplayName)
            .ToListAsync(cancellationToken);

        return fields.Select(f => f.ToModel()).ToList();
    }

    public async Task<RecordTypeField?> GetFieldAsync(Guid recordTypeId, Guid fieldId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypeFields.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == fieldId && f.RecordTypeId == recordTypeId, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<RecordTypeField> CreateFieldAsync(Guid recordTypeId, CreateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fieldKey = RecordFieldKey.Normalize(input.FieldKey ?? string.Empty);
        if (!RecordFieldKey.IsValid(fieldKey))
        {
            throw new RecordTypeValidationException("field_key must be lowercase snake_case (start with a letter, 1-64 chars).");
        }

        var displayName = (input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            throw new RecordTypeValidationException("display_name is required.");
        }

        if (!fieldTypeRegistry.TryGet(input.DataType ?? string.Empty, out var fieldType))
        {
            throw new RecordTypeValidationException($"Unknown data_type '{input.DataType}'.");
        }

        JsonElement normalizedConfig;
        try
        {
            normalizedConfig = fieldType.NormalizeConfig(input.Config);
        }
        catch (FieldConfigException ex)
        {
            throw new RecordTypeValidationException(ex.Message);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var typeEntity = await dbContext.RecordTypes
            .SingleOrDefaultAsync(t => t.Id == recordTypeId, cancellationToken)
            ?? throw new RecordTypeNotFoundException(recordTypeId);

        if (await dbContext.RecordTypeFields.AnyAsync(
                f => f.RecordTypeId == recordTypeId && f.FieldKey == fieldKey, cancellationToken))
        {
            throw new RecordTypeValidationException($"field_key '{fieldKey}' is already in use for this record type.");
        }

        var now = DateTimeOffset.UtcNow;
        var model = new RecordTypeField
        {
            Id = Guid.NewGuid(),
            RecordTypeId = recordTypeId,
            FieldKey = fieldKey,
            DisplayName = displayName,
            DataType = fieldType.DataType,
            Config = normalizedConfig,
            IsRequired = input.IsRequired,
            IsArchived = false,
            SortOrder = input.SortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var entity = new RecordTypeFieldEntity();
        entity.Apply(model);
        dbContext.RecordTypeFields.Add(entity);

        typeEntity.UpdatedAtUtc = now.UtcDateTime;
        typeEntity.UpdatedBy = actorId;

        dbContext.RecordTypeAuditLog.Add(BuildAudit(
            recordTypeId,
            RecordTypeAuditChangeKinds.FieldAdded,
            before: null,
            after: SerializeFieldSnapshot(model),
            actorId,
            now));

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordTypeField> UpdateFieldAsync(Guid recordTypeId, Guid fieldId, UpdateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var displayName = (input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            throw new RecordTypeValidationException("display_name is required.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypeFields
            .SingleOrDefaultAsync(f => f.Id == fieldId && f.RecordTypeId == recordTypeId, cancellationToken)
            ?? throw new RecordTypeFieldNotFoundException(fieldId);

        var typeEntity = await dbContext.RecordTypes
            .SingleOrDefaultAsync(t => t.Id == recordTypeId, cancellationToken)
            ?? throw new RecordTypeNotFoundException(recordTypeId);

        if (!fieldTypeRegistry.TryGet(entity.DataType, out var fieldType))
        {
            throw new RecordTypeValidationException($"Unknown data_type '{entity.DataType}' on existing field.");
        }

        JsonElement normalizedConfig;
        try
        {
            normalizedConfig = fieldType.NormalizeConfig(input.Config);
        }
        catch (FieldConfigException ex)
        {
            throw new RecordTypeValidationException(ex.Message);
        }

        var before = entity.ToModel();
        var now = DateTimeOffset.UtcNow;
        var changeKinds = new List<(string Kind, JsonElement Before, JsonElement After)>();

        if (!string.Equals(entity.DisplayName, displayName, StringComparison.Ordinal))
        {
            changeKinds.Add((RecordTypeAuditChangeKinds.FieldRenamed,
                Fields.FieldJsonHelpers.Serialize(new { display_name = entity.DisplayName }),
                Fields.FieldJsonHelpers.Serialize(new { display_name = displayName })));
            entity.DisplayName = displayName;
        }

        if (entity.IsRequired != input.IsRequired)
        {
            changeKinds.Add((RecordTypeAuditChangeKinds.FieldRequiredChanged,
                Fields.FieldJsonHelpers.Serialize(new { is_required = entity.IsRequired }),
                Fields.FieldJsonHelpers.Serialize(new { is_required = input.IsRequired })));
            entity.IsRequired = input.IsRequired;
        }

        if (entity.SortOrder != input.SortOrder)
        {
            changeKinds.Add((RecordTypeAuditChangeKinds.FieldReordered,
                Fields.FieldJsonHelpers.Serialize(new { sort_order = entity.SortOrder }),
                Fields.FieldJsonHelpers.Serialize(new { sort_order = input.SortOrder })));
            entity.SortOrder = input.SortOrder;
        }

        var previousConfigJson = entity.Config;
        var newConfigJson = normalizedConfig.GetRawText();
        if (!string.Equals(previousConfigJson, newConfigJson, StringComparison.Ordinal))
        {
            changeKinds.Add((RecordTypeAuditChangeKinds.FieldConfigChanged,
                RecordPersistenceMapper.ParseJson(previousConfigJson),
                normalizedConfig));
            entity.Config = newConfigJson;
        }

        entity.UpdatedAtUtc = now.UtcDateTime;
        typeEntity.UpdatedAtUtc = now.UtcDateTime;
        typeEntity.UpdatedBy = actorId;

        foreach (var change in changeKinds)
        {
            dbContext.RecordTypeAuditLog.Add(BuildAudit(
                recordTypeId, change.Kind,
                WrapFieldAudit(before.Id, change.Before),
                WrapFieldAudit(before.Id, change.After),
                actorId, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordTypeField> SetFieldArchivedAsync(Guid recordTypeId, Guid fieldId, bool archived, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordTypeFields
            .SingleOrDefaultAsync(f => f.Id == fieldId && f.RecordTypeId == recordTypeId, cancellationToken)
            ?? throw new RecordTypeFieldNotFoundException(fieldId);

        if (entity.IsArchived == archived)
        {
            return entity.ToModel();
        }

        var typeEntity = await dbContext.RecordTypes
            .SingleOrDefaultAsync(t => t.Id == recordTypeId, cancellationToken)
            ?? throw new RecordTypeNotFoundException(recordTypeId);

        var before = entity.ToModel();
        var now = DateTimeOffset.UtcNow;
        entity.IsArchived = archived;
        entity.UpdatedAtUtc = now.UtcDateTime;
        typeEntity.UpdatedAtUtc = now.UtcDateTime;
        typeEntity.UpdatedBy = actorId;

        dbContext.RecordTypeAuditLog.Add(BuildAudit(
            recordTypeId,
            archived ? RecordTypeAuditChangeKinds.FieldArchived : RecordTypeAuditChangeKinds.FieldUnarchived,
            WrapFieldAudit(before.Id, Fields.FieldJsonHelpers.Serialize(new { is_archived = before.IsArchived })),
            WrapFieldAudit(before.Id, Fields.FieldJsonHelpers.Serialize(new { is_archived = archived })),
            actorId, now));

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<IReadOnlyList<RecordTypeAuditEntry>> ListAuditAsync(Guid recordTypeId, int take, CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 500);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.RecordTypeAuditLog.AsNoTracking()
            .Where(a => a.RecordTypeId == recordTypeId)
            .OrderByDescending(a => a.ChangedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToModel()).ToList();
    }

    private static RecordTypeAuditEntity BuildAudit(
        Guid recordTypeId,
        string kind,
        JsonElement? before,
        JsonElement? after,
        Guid actorId,
        DateTimeOffset when) => new()
        {
            RecordTypeId = recordTypeId,
            ChangeKind = kind,
            Before = before?.GetRawText(),
            After = after?.GetRawText(),
            ChangedBy = actorId,
            ChangedAtUtc = when.UtcDateTime
        };

    private static JsonElement SerializeTypeSnapshot(RecordType type) =>
        Fields.FieldJsonHelpers.Serialize(new
        {
            id = type.Id,
            short_code = type.ShortCode,
            name = type.Name,
            description = type.Description,
            icon = type.Icon,
            color = type.Color,
            is_archived = type.IsArchived,
            is_system = type.IsSystem
        });

    private static JsonElement SerializeFieldSnapshot(RecordTypeField field) =>
        Fields.FieldJsonHelpers.Serialize(new
        {
            id = field.Id,
            field_key = field.FieldKey,
            display_name = field.DisplayName,
            data_type = field.DataType,
            is_required = field.IsRequired,
            sort_order = field.SortOrder
        });

    private static JsonElement WrapFieldAudit(Guid fieldId, JsonElement payload)
    {
        var json = $"{{\"field_id\":\"{fieldId}\",\"data\":{payload.GetRawText()}}}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

