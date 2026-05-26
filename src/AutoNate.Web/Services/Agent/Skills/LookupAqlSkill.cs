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
        "When asked about AQL: call get_aql_grammar to learn syntax (clauses, operators, " +
        "where-functions, relative-date forms, and the DON'T list of common mistakes), then " +
        "call describe_aql_entity for the target entity to read its columns, row functions, " +
        "value enums, and worked `examples` array. Pattern-match your draft against the " +
        "entity's examples — they are the canonical idioms. Never improvise function names, " +
        "column names, or date syntax from memory. Use list_saved_queries to find an existing " +
        "query the user has previously saved.";

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
                syntaxHint = SyntaxHint,
                relativeDateSyntax = RelativeDateSyntax,
                worked_examples = WorkedExamples,
                doNot = DoNotPatterns
            }
        };
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }

    // The grammar response is the chatbot's primary reference for "how do I
    // write AQL". The shape and worked examples below are tuned for what
    // LLMs actually get wrong: relative-date syntax (no `now - 7d`, no
    // `"2w ago"` strings) and BETWEEN bounds (must be date values, not
    // quoted strings). See LookupAqlSkill comment + AqlAssistSkill prompt.
    private const string SyntaxHint =
        "Shape: FROM <Entity> [WHERE <expr>] [ORDER BY <col> [ASC|DESC]] " +
        "[COLUMNS(<c1>, ...)] [GROUP(<col>, ...)] [LIMIT <n>]. " +
        "Clauses MUST appear in this order. String literals use double quotes. " +
        "Call describe_aql_entity for per-entity columns, row functions, value enums, " +
        "and entity-specific worked examples — prefer those over inventing field names.";

    private const string RelativeDateSyntax =
        "Date literals are RELATIVE to now. Three legal forms:\n" +
        "  -7d       integer + unit (h/d/w/m/y), negative = past, positive = future\n" +
        "  2w ago    positive integer + unit + 'ago', desugars to a negative offset\n" +
        "  NOW       current timestamp (equivalent to a zero-offset date)\n" +
        "There is NO 'now - 7d' arithmetic, NO bare 'today'/'yesterday'/'last week' keywords, " +
        "and NO string-quoted date forms like \"2w ago\" or \"now\". " +
        "BETWEEN/IN/comparison values on a date column MUST be one of the three forms above " +
        "(or NULL). A string literal compared to a date column is a validation error.";

    private static readonly object[] WorkedExamples = new object[]
    {
        new { intent = "Past two weeks of workflow executions",
              query = "FROM Flows WHERE StartDate >= -2w ORDER BY StartDate DESC" },
        new { intent = "Same idea using BETWEEN (date window)",
              query = "FROM Flows WHERE BETWEEN(StartDate, 2w ago, NOW) ORDER BY StartDate DESC" },
        new { intent = "Records of a specific type created today",
              query = "FROM Records WHERE RecordType = \"Car\" AND CreatedDate >= -1d" },
        new { intent = "Substring search",
              query = "FROM Records WHERE CONTAINS(Name, \"acme\")" },
        new { intent = "Status filter with enum value",
              query = "FROM Flows WHERE Status = \"In-progress\" ORDER BY StartDate DESC" },
        new { intent = "Top N by a numeric column",
              query = "FROM Flows WHERE Status = \"Completed\" ORDER BY DurationMs DESC LIMIT 10" },
        new { intent = "Counts grouped by a column",
              query = "FROM Flows COLUMNS(Status, COUNT() AS Total) GROUP(Status) ORDER BY Total DESC" }
    };

    private static readonly object[] DoNotPatterns = new object[]
    {
        new { wrong = "BETWEEN(StartDate, \"2w ago\", \"now\")",
              why   = "String literals are not date values. Use BETWEEN(StartDate, 2w ago, NOW) — unquoted." },
        new { wrong = "StartDate > now - 7d",
              why   = "AQL has no 'now' keyword and no infix date arithmetic. Write StartDate > -7d, or StartDate > 7d ago." },
        new { wrong = "StartDate >= \"2026-05-01\"",
              why   = "ISO date strings are not supported on date columns. Express the window relative to now: StartDate >= -3w (or whatever offset)." },
        new { wrong = "WHERE today() OR WHERE yesterday()",
              why   = "There are no date-keyword shortcuts. Use relative-date literals: today = >= -1d, yesterday = between -2d and -1d, etc." },
        new { wrong = "SELECT Name FROM ...",
              why   = "Projection is COLUMNS(Name), not SELECT. The shape is FROM/WHERE/ORDER BY/COLUMNS/GROUP/LIMIT — not SQL." }
    };

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
                // Canonical idioms for this entity — the chatbot should pattern-
                // match against these instead of inventing syntax from the column
                // and operator lists alone. Empty for entities that haven't opted
                // in; in that case fall back to the global syntax hint in
                // get_aql_grammar.
                examples = entity.Examples.Select(ex => new
                {
                    description = ex.Description,
                    query = ex.Query
                }).ToArray(),
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
