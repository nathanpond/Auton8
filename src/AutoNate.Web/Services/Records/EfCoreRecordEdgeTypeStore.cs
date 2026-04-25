using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using RecordEdgeTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordEdgeType;
using RecordEdgeTypeFieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordEdgeTypeField;

namespace AutoNate.Web.Services.Records;

public sealed class EfCoreRecordEdgeTypeStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFieldTypeRegistry fieldTypeRegistry) : IRecordEdgeTypeStore
{
    public async Task<IReadOnlyList<RecordEdgeType>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.RecordEdgeTypes.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(t => !t.IsArchived);
        }
        var entities = await query
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<RecordEdgeType?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordEdgeTypes.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<RecordEdgeType> CreateAsync(CreateRecordEdgeTypeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var shortCode = RecordTypeShortCode.Normalize(input.ShortCode ?? string.Empty);
        if (!RecordTypeShortCode.IsValid(shortCode))
        {
            throw new RecordEdgeValidationException("short_code must be 2-8 chars: start with a letter, then letters or digits.");
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new RecordEdgeValidationException("name is required.");
        }

        if (!RecordEdgeCardinality.IsValid(input.Cardinality))
        {
            throw new RecordEdgeValidationException(
                $"cardinality must be one of: one_to_one, one_to_many, many_to_one, many_to_many.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.RecordEdgeTypes.AnyAsync(t => t.ShortCode == shortCode, cancellationToken))
        {
            throw new RecordEdgeValidationException($"short_code '{shortCode}' is already in use.");
        }

        var now = DateTimeOffset.UtcNow;
        var model = new RecordEdgeType
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            Name = name,
            InverseName = string.IsNullOrWhiteSpace(input.InverseName) ? null : input.InverseName.Trim(),
            IsDirected = input.IsDirected,
            AllowSelfReference = input.AllowSelfReference,
            Cardinality = input.Cardinality,
            FromRecordTypeIds = input.FromRecordTypeIds is { Count: > 0 } from ? from.Distinct().ToList() : null,
            ToRecordTypeIds = input.ToRecordTypeIds is { Count: > 0 } to ? to.Distinct().ToList() : null,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var entity = new RecordEdgeTypeEntity();
        entity.Apply(model);
        dbContext.RecordEdgeTypes.Add(entity);

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordEdgeType> UpdateAsync(Guid id, UpdateRecordEdgeTypeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new RecordEdgeValidationException("name is required.");
        }
        if (!RecordEdgeCardinality.IsValid(input.Cardinality))
        {
            throw new RecordEdgeValidationException(
                $"cardinality must be one of: one_to_one, one_to_many, many_to_one, many_to_many.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordEdgeTypes
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new RecordEdgeTypeNotFoundException(id);

        entity.Name = name;
        entity.InverseName = string.IsNullOrWhiteSpace(input.InverseName) ? null : input.InverseName.Trim();
        entity.IsDirected = input.IsDirected;
        entity.AllowSelfReference = input.AllowSelfReference;
        entity.Cardinality = input.Cardinality;
        entity.FromRecordTypeIds = input.FromRecordTypeIds is { Count: > 0 } from ? from.Distinct().ToArray() : null;
        entity.ToRecordTypeIds = input.ToRecordTypeIds is { Count: > 0 } to ? to.Distinct().ToArray() : null;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordEdgeType> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordEdgeTypes
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new RecordEdgeTypeNotFoundException(id);

        if (entity.IsArchived == archived)
        {
            return entity.ToModel();
        }
        entity.IsArchived = archived;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<IReadOnlyList<RecordEdgeTypeField>> ListFieldsAsync(Guid edgeTypeId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.RecordEdgeTypeFields.AsNoTracking()
            .Where(f => f.EdgeTypeId == edgeTypeId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.DisplayName)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<RecordEdgeTypeField> CreateFieldAsync(Guid edgeTypeId, CreateRecordEdgeTypeFieldInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fieldKey = RecordFieldKey.Normalize(input.FieldKey ?? string.Empty);
        if (!RecordFieldKey.IsValid(fieldKey))
        {
            throw new RecordEdgeValidationException("field_key must be lowercase snake_case (start with a letter, 1-64 chars).");
        }

        var displayName = (input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            throw new RecordEdgeValidationException("display_name is required.");
        }

        if (!fieldTypeRegistry.TryGet(input.DataType ?? string.Empty, out var fieldType))
        {
            throw new RecordEdgeValidationException($"Unknown data_type '{input.DataType}'.");
        }

        JsonElement normalizedConfig;
        try
        {
            normalizedConfig = fieldType.NormalizeConfig(input.Config);
        }
        catch (FieldConfigException ex)
        {
            throw new RecordEdgeValidationException(ex.Message);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var typeExists = await dbContext.RecordEdgeTypes.AnyAsync(t => t.Id == edgeTypeId, cancellationToken);
        if (!typeExists) throw new RecordEdgeTypeNotFoundException(edgeTypeId);

        if (await dbContext.RecordEdgeTypeFields.AnyAsync(
                f => f.EdgeTypeId == edgeTypeId && f.FieldKey == fieldKey, cancellationToken))
        {
            throw new RecordEdgeValidationException($"field_key '{fieldKey}' is already in use for this edge type.");
        }

        var model = new RecordEdgeTypeField
        {
            Id = Guid.NewGuid(),
            EdgeTypeId = edgeTypeId,
            FieldKey = fieldKey,
            DisplayName = displayName,
            DataType = fieldType.DataType,
            Config = normalizedConfig,
            IsRequired = input.IsRequired,
            SortOrder = input.SortOrder
        };
        var entity = new RecordEdgeTypeFieldEntity();
        entity.Apply(model);
        dbContext.RecordEdgeTypeFields.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordEdgeTypeField> UpdateFieldAsync(Guid edgeTypeId, Guid fieldId, UpdateRecordEdgeTypeFieldInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var displayName = (input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            throw new RecordEdgeValidationException("display_name is required.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordEdgeTypeFields
            .SingleOrDefaultAsync(f => f.Id == fieldId && f.EdgeTypeId == edgeTypeId, cancellationToken)
            ?? throw new RecordEdgeValidationException($"Edge field '{fieldId}' was not found.");

        if (!fieldTypeRegistry.TryGet(entity.DataType, out var fieldType))
        {
            throw new RecordEdgeValidationException($"Unknown data_type '{entity.DataType}' on existing field.");
        }

        try
        {
            entity.Config = fieldType.NormalizeConfig(input.Config).GetRawText();
        }
        catch (FieldConfigException ex)
        {
            throw new RecordEdgeValidationException(ex.Message);
        }

        entity.DisplayName = displayName;
        entity.IsRequired = input.IsRequired;
        entity.SortOrder = input.SortOrder;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task DeleteFieldAsync(Guid edgeTypeId, Guid fieldId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordEdgeTypeFields
            .SingleOrDefaultAsync(f => f.Id == fieldId && f.EdgeTypeId == edgeTypeId, cancellationToken);
        if (entity is null) return;
        dbContext.RecordEdgeTypeFields.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
