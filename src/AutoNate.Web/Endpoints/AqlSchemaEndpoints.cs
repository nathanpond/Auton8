using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Endpoints;

// Catalog endpoints powering the QueryPage's autocomplete UI. Returns the
// registry-wide shape (entity names, columns, functions, operator table) and
// per-entity contextual fields (Records merges dynamic RecordType-specific
// fields when the user has typed a RecordType filter). The endpoints carry
// the same authorization shape as /api/query — any authenticated user can
// view the catalog. The metadata is non-sensitive: entity and column names
// are already implicitly exposed via /api/query error messages, and per-
// entity read enforcement happens at execute time, not catalog time.
public static class AqlSchemaEndpoints
{
    public static IEndpointRouteBuilder MapAqlSchemaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/aql/schema").RequireAuthorization();

        group.MapGet("/", (IQueryEntityRegistry registry) =>
            Results.Ok(BuildCatalog(registry)))
          .AuthorizedInHandler(
              "Read-only catalog of entity shapes used by the QueryPage's " +
              "autocomplete. Entity and column names are already implicitly " +
              "exposed via /api/query error messages; per-entity read " +
              "enforcement happens at /api/query execute time, not here. " +
              "No new permission is introduced.");

        group.MapGet("/entity", async (
            string name,
            string? recordType,
            IQueryEntityRegistry registry,
            CancellationToken ct) =>
        {
            if (!registry.TryGet(name, out var entity))
            {
                return Results.NotFound(new { error = $"Unknown entity '{name}'." });
            }

            var columns = await ResolveEntityColumnsAsync(entity, recordType, ct);
            var enums = await entity.GetDynamicColumnEnumsAsync(recordType, ct);

            var valueCompletions = enums.ToDictionary(
                kv => kv.Key,
                kv => new ValueCompletionDto(kv.Value, ClosedSet: true),
                StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new EntityContextResponse(
                Entity: entity.Name,
                ResolvedRecordType: recordType,
                Columns: columns.Select(ToColumnDto).ToList(),
                ValueCompletions: valueCompletions));
        }).AuthorizedInHandler(
              "Same rationale as the catalog root: read-only entity shape, " +
              "no new permission.");

        return app;
    }

    // ---- Catalog builder -------------------------------------------------

    private static readonly IReadOnlyList<string> ClauseKeywords = new[]
    {
        "FROM", "WHERE", "ORDER BY", "COLUMNS", "GROUP", "LIMIT",
        "AND", "OR", "ASC", "DESC", "AS"
    };

    private static readonly IReadOnlyList<AggregateDto> GlobalAggregates = new[]
    {
        new AggregateDto("COUNT", RequiresArgument: false),
        new AggregateDto("MIN",   RequiresArgument: true),
        new AggregateDto("MAX",   RequiresArgument: true),
        new AggregateDto("AVG",   RequiresArgument: true),
        new AggregateDto("MEDIAN", RequiresArgument: true)
    };

    private static readonly IReadOnlyList<string> WhereFunctions = new[]
    {
        "CONTAINS", "IN", "BETWEEN"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OperatorsByDataType =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = new[] { "=", "!=", "~" },
            ["number"] = new[] { "=", "!=", "<", "<=", ">", ">=" },
            ["bool"]   = new[] { "=", "!=" },
            ["date"]   = new[] { "=", "!=", "<", "<=", ">", ">=" },
            ["json"]   = new[] { "=", "!=" }
        };

    private static readonly IReadOnlyList<string> RelativeDateUnits = new[]
    {
        "h", "d", "w", "m", "y"
    };

    private static SchemaResponse BuildCatalog(IQueryEntityRegistry registry)
    {
        var entities = registry.EntityNames
            .Select(name => registry.TryGet(name, out var e) ? e : null)
            .Where(e => e is not null)
            .Select(BuildEntityDto!)
            .ToList();

        return new SchemaResponse(
            ClauseKeywords: ClauseKeywords,
            GlobalAggregates: GlobalAggregates,
            WhereFunctions: WhereFunctions,
            OperatorsByDataType: OperatorsByDataType,
            RelativeDateUnits: RelativeDateUnits,
            Entities: entities);
    }

    private static EntityDto BuildEntityDto(IQueryEntity entity)
    {
        var rowFunctions = entity.RowFunctions
            .Select(fn => new RowFunctionDto(
                Name: fn,
                AcceptsArgument: entity.RowFunctionAcceptsArgument(fn),
                DataType: DataTypeName(entity.RowFunctionDataType(fn)),
                Arguments: entity.RowFunctionArguments(fn).ToList()))
            .ToList();

        // Only Records has dynamic fields today. The RecordType column is the
        // scoping field — when the user types `RecordType = "X"`, the
        // autocomplete uses that literal to call the entity context endpoint
        // with `?recordType=X`. Future entities that gain dynamic schema can
        // declare their own scoping field via this hook.
        var hasDynamic = string.Equals(entity.Name, "Records", StringComparison.OrdinalIgnoreCase);
        var recordTypeFilterField = hasDynamic ? "RecordType" : null;

        return new EntityDto(
            Name: entity.Name,
            StaticColumns: entity.StaticSchema.Select(ToColumnDto).ToList(),
            AllowedWhereFunctions: entity.AllowedFunctions.ToList(),
            RowFunctions: rowFunctions,
            HasDynamicFields: hasDynamic,
            RecordTypeFilterField: recordTypeFilterField,
            AcceptsEntityArgument: entity.AcceptsEntityArgument,
            RequiresEntityArgument: entity.RequiresEntityArgument,
            EntityArgumentHint: entity.EntityArgumentHint);
    }

    // ---- Per-entity column resolution ------------------------------------

    private static async Task<IReadOnlyList<QueryColumn>> ResolveEntityColumnsAsync(
        IQueryEntity entity, string? recordType, CancellationToken ct)
    {
        // For Records, the merged static + dynamic schema lives behind
        // PrepareAsync — call it with a synthetic query so the entity does its
        // normal name→id resolution and dynamic-field merge, then read
        // prepared.Schema. For every other entity, the static schema is
        // exhaustive and we skip the PrepareAsync round trip entirely.
        if (string.Equals(entity.Name, "Records", StringComparison.OrdinalIgnoreCase))
        {
            AqlWhere? where = string.IsNullOrEmpty(recordType)
                ? null
                : new AqlCompare("RecordType", "=", new AqlString(recordType));
            var syntheticQuery = new AqlQuery(
                Entity: entity.Name,
                Where: where,
                OrderBy: Array.Empty<AqlOrderItem>(),
                Columns: null,
                Group: null,
                Limit: null);
            var prepared = await entity.PrepareAsync(syntheticQuery, ct);
            return prepared.Schema;
        }
        return entity.StaticSchema;
    }

    private static ColumnDto ToColumnDto(QueryColumn c) =>
        new(Name: c.Name,
            DataType: DataTypeName(c.DataType),
            IsAggregable: c.IsAggregable,
            IsSystem: c.IsSystem);

    private static string DataTypeName(QueryDataType t) => t.ToString().ToLowerInvariant();

    // ---- DTOs ------------------------------------------------------------

    public sealed record SchemaResponse(
        IReadOnlyList<string> ClauseKeywords,
        IReadOnlyList<AggregateDto> GlobalAggregates,
        IReadOnlyList<string> WhereFunctions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> OperatorsByDataType,
        IReadOnlyList<string> RelativeDateUnits,
        IReadOnlyList<EntityDto> Entities);

    public sealed record AggregateDto(string Name, bool RequiresArgument);

    public sealed record EntityDto(
        string Name,
        IReadOnlyList<ColumnDto> StaticColumns,
        IReadOnlyList<string> AllowedWhereFunctions,
        IReadOnlyList<RowFunctionDto> RowFunctions,
        bool HasDynamicFields,
        string? RecordTypeFilterField,
        // Parameterized-FROM metadata (Phase 2 of the Data Stores plan).
        // The chatbot's describe_aql_entity tool and the SPA's monaco
        // completion logic both branch on AcceptsEntityArgument to know
        // whether to suggest `Entity("...")` or bare `Entity`.
        bool AcceptsEntityArgument,
        bool RequiresEntityArgument,
        string? EntityArgumentHint);

    public sealed record RowFunctionDto(
        string Name,
        bool AcceptsArgument,
        string DataType,
        IReadOnlyList<string> Arguments);

    public sealed record ColumnDto(
        string Name,
        string DataType,
        bool IsAggregable,
        bool IsSystem);

    public sealed record EntityContextResponse(
        string Entity,
        string? ResolvedRecordType,
        IReadOnlyList<ColumnDto> Columns,
        IReadOnlyDictionary<string, ValueCompletionDto> ValueCompletions);

    public sealed record ValueCompletionDto(IReadOnlyList<string> Values, bool ClosedSet);
}
