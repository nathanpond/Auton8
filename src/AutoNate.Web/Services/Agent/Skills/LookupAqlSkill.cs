using System.Text.Json;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only AQL diagnostics. Exposes the saved-query catalog plus the live
// AQL grammar and per-entity schema so the model can answer questions like
// "what columns does the Records entity have?" or "what filter operators work
// on dates?" without baking grammar into the system prompt. Phase 2's
// AqlAssistSkill leans on these tools to draft validated queries.
public sealed class LookupAqlSkill : IAgentSkill
{
    public string Name => "lookup-aql";

    public string Description =>
        "Browse saved AQL queries and introspect the AQL grammar + entity schemas.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupAqlSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_saved_queries",
                Description: "List saved AQL queries visible to the current user: their own plus every shared row.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "take": { "type": "integer", "minimum": 1, "maximum": 200 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListSavedQueriesAsync),

            new AgentTool(
                Name: "get_saved_query",
                Description: "Fetch a saved query by id. Returns null if the actor is neither owner nor a shared-visibility grantee.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string", "description": "Saved-query GUID." }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetSavedQueryAsync),

            new AgentTool(
                Name: "get_aql_grammar",
                Description: "Return the AQL grammar surface: clause keywords, aggregate functions, where functions, operators by data type, relative-date units, and the list of queryable entity names. Call describe_aql_entity for per-entity columns.",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeGetGrammarAsync),

            new AgentTool(
                Name: "describe_aql_entity",
                Description: "Describe one queryable entity: columns (static + dynamic for Records), allowed where functions, row functions. For the Records entity pass recordType to merge in custom fields for that type.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string", "description": "Entity name (case-insensitive), e.g. Records, Flows, Notes." },
                        "recordType": { "type": ["string", "null"], "description": "Optional RecordType name; only meaningful when name='Records'." }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDescribeEntityAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "When asked about AQL: call get_aql_grammar to learn syntax (clauses, operators, functions) and describe_aql_entity to learn the columns an entity exposes. The grammar and schemas are the source of truth — do not improvise function names or column names. Use list_saved_queries to find an existing query the user has previously saved.";

    private static async Task<JsonElement> InvokeListSavedQueriesAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var take = args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, 200)
            : 50;

        var store = context.Services.GetRequiredService<ISavedQueryStore>();
        var rows = await store.ListForActorAsync(context.Session.UserId, ct);
        var items = rows.Take(take).Select(r => new
        {
            id = r.Id,
            name = r.Name,
            description = r.Description,
            queryText = r.QueryText,
            isShared = r.IsShared,
            isOwn = r.OwnerUserId == context.Session.UserId,
            ownerUserId = r.OwnerUserId,
            updatedAtUtc = r.UpdatedAtUtc
        }).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            kind = "saved_queries",
            source = "ISavedQueryStore",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetSavedQueryAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idElem) || idElem.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idElem.GetString(), out var id))
        {
            return Error("get_saved_query", "id is required and must be a GUID.");
        }

        var store = context.Services.GetRequiredService<ISavedQueryStore>();
        var row = await store.GetForActorAsync(id, context.Session.UserId, ct);
        if (row is null)
        {
            return Error("get_saved_query", $"Saved query '{id}' not visible to current user.");
        }
        return JsonSerializer.SerializeToElement(new
        {
            kind = "saved_query",
            source = "ISavedQueryStore",
            data = new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description,
                queryText = row.QueryText,
                isShared = row.IsShared,
                isOwn = row.OwnerUserId == context.Session.UserId,
                ownerUserId = row.OwnerUserId,
                createdAtUtc = row.CreatedAtUtc,
                updatedAtUtc = row.UpdatedAtUtc
            }
        });
    }

    private static Task<JsonElement> InvokeGetGrammarAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var registry = context.Services.GetRequiredService<IQueryEntityRegistry>();
        var result = new
        {
            kind = "aql_grammar",
            source = "IQueryEntityRegistry",
            data = new
            {
                clauseKeywords = ClauseKeywords,
                aggregates = Aggregates,
                whereFunctions = WhereFunctions,
                operatorsByDataType = OperatorsByDataType,
                relativeDateUnits = RelativeDateUnits,
                entityNames = registry.EntityNames,
                syntaxHint = "Shape: FROM <Entity> [WHERE <expr>] [ORDER BY <col> [ASC|DESC]] [COLUMNS <c1>, ...] [GROUP <col>] [LIMIT <n>]. Strings in double quotes. Relative dates as `now - 7d`. Use describe_aql_entity for per-entity columns and row-function vocabularies."
            }
        };
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }

    private static async Task<JsonElement> InvokeDescribeEntityAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var name = args.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error("describe_aql_entity", "name is required.");
        }
        string? recordType = args.TryGetProperty("recordType", out var rt) && rt.ValueKind == JsonValueKind.String
            ? rt.GetString()
            : null;

        var registry = context.Services.GetRequiredService<IQueryEntityRegistry>();
        if (!registry.TryGet(name, out var entity))
        {
            return Error("describe_aql_entity", $"Unknown entity '{name}'. Call get_aql_grammar for the full list.");
        }

        IReadOnlyList<QueryColumn> columns;
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
            columns = prepared.Schema;
        }
        else
        {
            columns = entity.StaticSchema;
        }

        var rowFunctions = entity.RowFunctions
            .Select(fn => new
            {
                name = fn,
                acceptsArgument = entity.RowFunctionAcceptsArgument(fn),
                dataType = entity.RowFunctionDataType(fn).ToString().ToLowerInvariant(),
                arguments = entity.RowFunctionArguments(fn)
            })
            .ToArray();

        var enums = await entity.GetDynamicColumnEnumsAsync(recordType, ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "aql_entity",
            source = "IQueryEntity",
            data = new
            {
                name = entity.Name,
                resolvedRecordType = recordType,
                columns = columns.Select(c => new
                {
                    name = c.Name,
                    dataType = c.DataType.ToString().ToLowerInvariant(),
                    isAggregable = c.IsAggregable,
                    isSystem = c.IsSystem
                }).ToArray(),
                allowedWhereFunctions = entity.AllowedFunctions,
                rowFunctions,
                valueEnums = enums.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                hasDynamicFields = string.Equals(entity.Name, "Records", StringComparison.OrdinalIgnoreCase)
            }
        });
    }

    private static readonly string[] ClauseKeywords = new[]
    {
        "FROM", "WHERE", "ORDER BY", "COLUMNS", "GROUP", "LIMIT",
        "AND", "OR", "ASC", "DESC", "AS"
    };

    private static readonly object[] Aggregates = new object[]
    {
        new { name = "COUNT", requiresArgument = false },
        new { name = "MIN", requiresArgument = true },
        new { name = "MAX", requiresArgument = true },
        new { name = "AVG", requiresArgument = true },
        new { name = "MEDIAN", requiresArgument = true }
    };

    private static readonly string[] WhereFunctions = new[] { "CONTAINS", "IN", "BETWEEN" };

    private static readonly Dictionary<string, string[]> OperatorsByDataType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = new[] { "=", "!=", "~" },
            ["number"] = new[] { "=", "!=", "<", "<=", ">", ">=" },
            ["bool"] = new[] { "=", "!=" },
            ["date"] = new[] { "=", "!=", "<", "<=", ">", ">=" },
            ["json"] = new[] { "=", "!=" }
        };

    private static readonly string[] RelativeDateUnits = new[] { "h", "d", "w", "m", "y" };

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
