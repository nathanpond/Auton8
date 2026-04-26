using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using EntityEdgeEntity = AutoNate.Web.Persistence.Scaffolded.EntityEdge;
using RecordEdgeEntity = AutoNate.Web.Persistence.Scaffolded.RecordEdge;

namespace AutoNate.Web.Services.Records;

public sealed class EfCoreRecordEdgeStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFieldTypeRegistry fieldTypeRegistry) : IRecordEdgeStore
{
    public async Task<RecordEdge> CreateAsync(CreateRecordEdgeInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var edgeType = await dbContext.RecordEdgeTypes
            .SingleOrDefaultAsync(t => t.Id == input.EdgeTypeId, cancellationToken)
            ?? throw new RecordEdgeTypeNotFoundException(input.EdgeTypeId);

        if (edgeType.IsArchived)
        {
            throw new RecordEdgeValidationException("Cannot create edges of an archived edge type.");
        }

        if (!edgeType.AllowSelfReference && input.FromRecordId == input.ToRecordId)
        {
            throw new RecordEdgeValidationException("This edge type does not allow self-references.");
        }

        var fromRecord = await dbContext.Records.AsNoTracking()
            .Where(r => r.Id == input.FromRecordId)
            .Select(r => new { r.Id, r.RecordTypeId, r.IsArchived })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new RecordEdgeValidationException($"From record '{input.FromRecordId}' was not found.");

        var toRecord = await dbContext.Records.AsNoTracking()
            .Where(r => r.Id == input.ToRecordId)
            .Select(r => new { r.Id, r.RecordTypeId, r.IsArchived })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new RecordEdgeValidationException($"To record '{input.ToRecordId}' was not found.");

        if (fromRecord.IsArchived || toRecord.IsArchived)
        {
            throw new RecordEdgeValidationException("Cannot link archived records.");
        }

        if (edgeType.FromRecordTypeIds is { Length: > 0 } fromAllowed &&
            !fromAllowed.Contains(fromRecord.RecordTypeId))
        {
            throw new RecordEdgeValidationException(
                "Source record's type is not allowed for this edge type.");
        }
        if (edgeType.ToRecordTypeIds is { Length: > 0 } toAllowed &&
            !toAllowed.Contains(toRecord.RecordTypeId))
        {
            throw new RecordEdgeValidationException(
                "Target record's type is not allowed for this edge type.");
        }

        // Duplicate detection. The unique index covers (edge_type_id, from, to).
        // For undirected types, also reject existing edges in the reverse
        // direction so (A,B) and (B,A) don't both exist for the same type.
        var duplicateExists = await dbContext.RecordEdges.AnyAsync(
            e => e.EdgeTypeId == edgeType.Id &&
                 e.FromRecordId == input.FromRecordId &&
                 e.ToRecordId == input.ToRecordId,
            cancellationToken);
        if (duplicateExists)
        {
            throw new RecordEdgeValidationException("An edge of this type already exists between these records.");
        }
        if (!edgeType.IsDirected)
        {
            var reverseExists = await dbContext.RecordEdges.AnyAsync(
                e => e.EdgeTypeId == edgeType.Id &&
                     e.FromRecordId == input.ToRecordId &&
                     e.ToRecordId == input.FromRecordId,
                cancellationToken);
            if (reverseExists)
            {
                throw new RecordEdgeValidationException(
                    "Undirected edge already exists between these records.");
            }
        }

        await EnforceCardinalityAsync(dbContext, edgeType.Id, edgeType.Cardinality, input.FromRecordId, input.ToRecordId, cancellationToken);

        // Validate edge data against edge type fields.
        var fields = await dbContext.RecordEdgeTypeFields.AsNoTracking()
            .Where(f => f.EdgeTypeId == edgeType.Id)
            .ToListAsync(cancellationToken);
        var normalizedData = ValidateEdgeData(fields, input.Data);

        var now = DateTimeOffset.UtcNow;
        var edgeId = Guid.NewGuid();
        var dataJson = normalizedData.GetRawText();
        var entity = new RecordEdgeEntity
        {
            Id = edgeId,
            EdgeTypeId = edgeType.Id,
            FromRecordId = input.FromRecordId,
            ToRecordId = input.ToRecordId,
            Data = dataJson,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId
        };
        dbContext.RecordEdges.Add(entity);

        // Phase 7: shadow into entity_edges so the unified graph stays in sync
        // for selectors and traversals. Same primary key keeps dedup trivial.
        dbContext.EntityEdges.Add(new EntityEdgeEntity
        {
            Id = edgeId,
            EdgeKind = edgeType.ShortCode,
            FromKind = EntityKinds.Record,
            FromId = input.FromRecordId.ToString(),
            ToKind = EntityKinds.Record,
            ToId = input.ToRecordId.ToString(),
            Data = dataJson,
            CreatedAtUtc = now.UtcDateTime,
            CreatedBy = actorId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task DeleteAsync(Guid edgeId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordEdges.SingleOrDefaultAsync(e => e.Id == edgeId, cancellationToken);
        if (entity is null) return;
        dbContext.RecordEdges.Remove(entity);

        var shadow = await dbContext.EntityEdges.SingleOrDefaultAsync(e => e.Id == edgeId, cancellationToken);
        if (shadow is not null)
        {
            dbContext.EntityEdges.Remove(shadow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecordEdge>> ListForRecordAsync(
        Guid recordId,
        EdgeDirection direction,
        Guid? edgeTypeId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.RecordEdges.AsNoTracking().AsQueryable();

        switch (direction)
        {
            case EdgeDirection.Outgoing:
                query = query.Where(e => e.FromRecordId == recordId);
                break;
            case EdgeDirection.Incoming:
                query = query.Where(e => e.ToRecordId == recordId);
                break;
            case EdgeDirection.Both:
            default:
                query = query.Where(e => e.FromRecordId == recordId || e.ToRecordId == recordId);
                break;
        }

        if (edgeTypeId is { } typeId)
        {
            query = query.Where(e => e.EdgeTypeId == typeId);
        }

        var entities = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<IReadOnlyList<TraverseResultRow>> TraverseAsync(
        TraverseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.StartRecordIds is null || request.StartRecordIds.Count == 0)
        {
            return Array.Empty<TraverseResultRow>();
        }

        var maxHops = Math.Clamp(request.MaxHops, 1, 3);
        var startIds = request.StartRecordIds.Distinct().ToArray();
        var edgeTypeIds = request.EdgeTypeIds is { Count: > 0 }
            ? request.EdgeTypeIds.Distinct().ToArray()
            : Array.Empty<Guid>();

        // Build the recursive step depending on direction. The CTE seeds itself
        // with the start ids at hop 0, then each iteration walks one edge in the
        // requested direction(s). We keep track of the minimum hop count per
        // record so the result reflects shortest path.
        var stepSql = request.Direction switch
        {
            EdgeDirection.Outgoing =>
                "SELECT e.to_record_id, walk.hop + 1 FROM record_edges e " +
                "JOIN walk ON e.from_record_id = walk.record_id",
            EdgeDirection.Incoming =>
                "SELECT e.from_record_id, walk.hop + 1 FROM record_edges e " +
                "JOIN walk ON e.to_record_id = walk.record_id",
            _ =>
                "SELECT CASE WHEN e.from_record_id = walk.record_id THEN e.to_record_id ELSE e.from_record_id END, walk.hop + 1 " +
                "FROM record_edges e " +
                "JOIN walk ON e.from_record_id = walk.record_id OR e.to_record_id = walk.record_id"
        };

        var edgeTypeFilter = edgeTypeIds.Length > 0
            ? " AND e.edge_type_id = ANY({2}::uuid[])"
            : string.Empty;

        var sql =
            $@"WITH RECURSIVE walk(record_id, hop) AS (
                  SELECT unnest({{0}}::uuid[]), 0
                  UNION ALL
                  {stepSql}
                  WHERE walk.hop < {{1}}{edgeTypeFilter}
              )
              SELECT record_id AS ""RecordId"", MIN(hop)::int AS ""Hops""
              FROM walk
              GROUP BY record_id";

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var parameters = new List<object> { startIds, maxHops };
        if (edgeTypeIds.Length > 0)
        {
            parameters.Add(edgeTypeIds);
        }

        var rows = await dbContext.Database
            .SqlQueryRaw<TraverseResultRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        return rows;
    }

    private static async Task EnforceCardinalityAsync(
        AutoNateDbContext dbContext,
        Guid edgeTypeId,
        string cardinality,
        Guid fromRecordId,
        Guid toRecordId,
        CancellationToken cancellationToken)
    {
        switch (cardinality)
        {
            case RecordEdgeCardinality.OneToOne:
                if (await dbContext.RecordEdges.AnyAsync(
                        e => e.EdgeTypeId == edgeTypeId && e.FromRecordId == fromRecordId, cancellationToken))
                {
                    throw new RecordEdgeValidationException(
                        "one_to_one: source already has an edge of this type.");
                }
                if (await dbContext.RecordEdges.AnyAsync(
                        e => e.EdgeTypeId == edgeTypeId && e.ToRecordId == toRecordId, cancellationToken))
                {
                    throw new RecordEdgeValidationException(
                        "one_to_one: target already has an edge of this type.");
                }
                break;

            case RecordEdgeCardinality.OneToMany:
                if (await dbContext.RecordEdges.AnyAsync(
                        e => e.EdgeTypeId == edgeTypeId && e.ToRecordId == toRecordId, cancellationToken))
                {
                    throw new RecordEdgeValidationException(
                        "one_to_many: target already has an edge of this type.");
                }
                break;

            case RecordEdgeCardinality.ManyToOne:
                if (await dbContext.RecordEdges.AnyAsync(
                        e => e.EdgeTypeId == edgeTypeId && e.FromRecordId == fromRecordId, cancellationToken))
                {
                    throw new RecordEdgeValidationException(
                        "many_to_one: source already has an edge of this type.");
                }
                break;

            case RecordEdgeCardinality.ManyToMany:
            default:
                break;
        }
    }

    private JsonElement ValidateEdgeData(
        IReadOnlyList<Persistence.Scaffolded.RecordEdgeTypeField> fieldsRaw,
        JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object && payload.ValueKind != JsonValueKind.Undefined)
        {
            throw new RecordEdgeValidationException("data must be a JSON object.");
        }

        var fields = fieldsRaw.Select(f => f.ToModel()).ToList();
        var fieldsByKey = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);

        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in payload.EnumerateObject())
            {
                if (!fieldsByKey.ContainsKey(prop.Name))
                {
                    throw new RecordEdgeValidationException($"Unknown edge field '{prop.Name}'.");
                }
            }
        }

        var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var field in fields)
        {
            if (!fieldTypeRegistry.TryGet(field.DataType, out var fieldType))
            {
                throw new RecordEdgeValidationException($"Unknown data type '{field.DataType}' on edge field '{field.FieldKey}'.");
            }

            JsonElement raw = default;
            var hasValue = payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty(field.FieldKey, out raw);

            if (!hasValue)
            {
                if (field.IsRequired)
                {
                    errors.Add($"Edge field '{field.FieldKey}' is required.");
                }
                continue;
            }

            var result = fieldType.ValidateValue(raw, field.Config, field.IsRequired, out var normalized);
            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                {
                    errors.Add($"{field.FieldKey}: {err.Message}");
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
            throw new RecordEdgeValidationException(string.Join("; ", errors));
        }

        var json = "{" + string.Join(",", output.Select(kvp =>
            $"\"{kvp.Key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\":{kvp.Value}")) + "}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
