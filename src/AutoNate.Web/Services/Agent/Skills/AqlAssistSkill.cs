using System.Diagnostics;
using System.Text.Json;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 2 AQL write-help. The model drafts AQL itself (no suggest tool — that
// would just round-trip text), then calls validate_aql to surface errors and
// optionally run_aql to preview results. Inserting the query into the
// QueryPage editor goes through InspectPageSkill.apply_page_action — this
// skill only deals with the query string itself.
//
// Both tools share the same gating posture as /api/query: any authenticated
// user can validate and run AQL because per-entity reads are enforced
// downstream (record visibility SQL filter; WorkflowModel:View kind gate).
public sealed class AqlAssistSkill : IAgentSkill
{
    public const string ValidateToolName = "validate_aql";
    public const string RunToolName = "run_aql";

    public string Name => "aql-assist";

    public string Description =>
        "Validate and dry-run AQL queries on behalf of the user. Pair with apply_page_action to insert a draft into the QueryPage editor.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public AqlAssistSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: ValidateToolName,
                Description: "Parse + type-check an AQL query string without executing. Returns errors with friendly messages so you can correct a draft before proposing it to the user. Side-effect free.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "queryText": { "type": "string", "description": "The AQL query string to validate." }
                      },
                      "required": ["queryText"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeValidateAsync),

            new AgentTool(
                Name: RunToolName,
                Description: "Execute an AQL query against the gated executor and return columns + rows + truncation flag. Caps at 200 rows. Use sparingly — only when the user has asked for results, not while drafting.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "queryText": { "type": "string", "description": "The AQL query string to execute." },
                        "maxRows": { "type": "integer", "minimum": 1, "maximum": 200, "description": "Row cap (1-200, default 50)." }
                      },
                      "required": ["queryText"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRunAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "AQL assistance protocol: " +
        "(1) Call get_aql_grammar and describe_aql_entity FIRST to learn syntax + columns — never improvise function names or columns from memory. " +
        "(2) Draft a query in your reply, then call validate_aql to catch errors; fix and revalidate until clean. " +
        "(3) Only call run_aql when the user has asked for actual results. " +
        "(4) When the user is on the QueryPage, propose insertion via apply_page_action set_aql_text with confirmed=false first; commit only after explicit user approval. " +
        "(5) Never run a query as a side effect of drafting.";

    private static async Task<JsonElement> InvokeValidateAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var queryText = ReadString(args, "queryText");
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Envelope("aql_validation", new
            {
                queryText = string.Empty,
                ok = false,
                errors = QueryTextRequiredErrors
            });
        }

        try
        {
            var ast = AqlParser.Parse(queryText);
            var registry = context.Services.GetRequiredService<IQueryEntityRegistry>();
            var validator = new AqlValidator(registry);
            var prepared = await validator.ValidateAsync(ast, hardCap: 1000, ct);

            return Envelope("aql_validation", new
            {
                queryText,
                ok = prepared.ValidationErrors.Count == 0,
                entity = prepared.Entity.Name,
                columns = prepared.Schema.Select(c => new
                {
                    name = c.Name,
                    dataType = c.DataType.ToString().ToLowerInvariant(),
                    isAggregable = c.IsAggregable,
                    isSystem = c.IsSystem
                }).ToArray(),
                errors = prepared.ValidationErrors
            });
        }
        catch (AqlValidationException ex)
        {
            return Envelope("aql_validation", new
            {
                queryText,
                ok = false,
                errors = ex.Errors
            });
        }
        catch (Exception ex)
        {
            return Envelope("aql_validation", new
            {
                queryText,
                ok = false,
                errors = new[] { $"Parse error: {ex.Message}" }
            });
        }
    }

    private static async Task<JsonElement> InvokeRunAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var queryText = ReadString(args, "queryText");
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Envelope("aql_run_failed", new
            {
                queryText = string.Empty,
                ok = false,
                errors = QueryTextRequiredErrors
            });
        }
        var maxRows = args.TryGetProperty("maxRows", out var mr) && mr.ValueKind == JsonValueKind.Number
            ? Math.Clamp(mr.GetInt32(), 1, 200)
            : 50;

        var executor = context.Services.GetRequiredService<IAqlExecutor>();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await executor.ExecuteAsync(queryText, context.Session.User, hardCap: maxRows, ct);
            stopwatch.Stop();

            return Envelope("aql_run_result", new
            {
                queryText,
                ok = true,
                columns = result.Columns.Select(c => new
                {
                    name = c.Name,
                    dataType = c.DataType.ToString().ToLowerInvariant()
                }).ToArray(),
                rows = result.Rows,
                totalCount = result.TotalCount,
                truncated = result.Truncated,
                durationMs = result.DurationMs
            });
        }
        catch (AqlValidationException ex)
        {
            return Envelope("aql_run_failed", new
            {
                queryText,
                ok = false,
                errors = ex.Errors
            });
        }
    }

    private static readonly string[] QueryTextRequiredErrors = new[] { "queryText is required." };

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement Envelope(string kind, object data) =>
        JsonSerializer.SerializeToElement(new { kind, source = "AqlAssistSkill", data });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
