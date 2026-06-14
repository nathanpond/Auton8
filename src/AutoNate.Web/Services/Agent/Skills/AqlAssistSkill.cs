using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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
                Description:
                    "Parse + type-check an AQL query string without executing. Side-effect free. " +
                    "Returns `sourceColumns` (the entity's static schema — the fields/aggregates AVAILABLE inside the query, e.g. for use in WHERE / ORDER BY / aggregate-function args) " +
                    "AND `resultColumns` (the POST-PROJECTION schema — the columns the executed query's rows will actually have, derived from the COLUMNS(...) clause). " +
                    "Use `resultColumns` when binding the query to a chart widget or anything that consumes the result row stream (e.g. savedQueryLabelColumn / savedQueryValueColumn must reference a `resultColumns` name, NOT a `sourceColumns` name). " +
                    "Use `sourceColumns` to learn what fields you can reference INSIDE the query. Without a COLUMNS(...) clause the two lists are identical. " +
                    "⚠ Clause order is FROM → WHERE → ORDER BY → COLUMNS → GROUP → LIMIT. " +
                    "ORDER BY-by-alias is entity-dependent: Dataset queries accept `ORDER BY <alias>` where <alias> was introduced via COLUMNS(... AS <alias>); other entities (Records, Flows, Notes, Workflow*) resolve ORDER BY only against the source schema, so for those repeat the expression instead — `ORDER BY AVG(value) DESC COLUMNS(date, AVG(value) AS avg_value) GROUP(date)`.",
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
        "AQL assistance protocol — every step is mandatory:\n" +
        "(1) BEFORE drafting any AQL, call get_aql_grammar AND describe_aql_entity for the target entity. " +
        "Read its `examples` array and the grammar's `worked_examples` + `doNot` list. " +
        "Pattern-match your draft against those — never improvise field names, function names, " +
        "or date syntax from memory. Date literals are only `-7d` / `2w ago` / `NOW` — never " +
        "string-quoted (`\"now\"`, `\"2w ago\"`) and never infix arithmetic (`now - 7d`).\n" +
        "(2) AFTER drafting, ALWAYS call validate_aql on the EXACT query text before showing it to " +
        "the user or proposing it via apply_page_action. Treat ok=false as a hard block: read the " +
        "errors and the `hints` array, fix the query, and revalidate. Repeat until ok=true. " +
        "Never propose, paste, or commit an unvalidated query.\n" +
        "(3) Only call run_aql when the user has explicitly asked for actual results — not while " +
        "drafting, not to \"check\" a query (validate_aql is for that).\n" +
        "(4) On the QueryPage, propose insertion via apply_page_action set_aql_text with " +
        "confirmed=false first; commit only after explicit user approval.\n" +
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
                errors = QueryTextRequiredErrors,
                hints = Array.Empty<string>()
            });
        }

        try
        {
            var ast = AqlParser.Parse(queryText);
            var registry = context.Services.GetRequiredService<IQueryEntityRegistry>();
            var validator = new AqlValidator(registry);
            var prepared = await validator.ValidateAsync(ast, hardCap: 1000, ct);

            var ok = prepared.ValidationErrors.Count == 0;
            // Source schema = what fields/aggregates the query CAN reference
            // inside its clauses. Result schema = what columns the executed
            // query's row stream will actually have (post-COLUMNS projection).
            // Both are emitted so callers can pick the right one for their
            // use case — widget axis bindings need resultColumns, query
            // authoring uses sourceColumns.
            var resultSchema = AqlResultSchema.Derive(ast, prepared.Schema);
            return Envelope("aql_validation", new
            {
                queryText,
                ok,
                entity = prepared.Entity.Name,
                sourceColumns = prepared.Schema.Select(c => new
                {
                    name = c.Name,
                    dataType = c.DataType.ToString().ToLowerInvariant(),
                    isAggregable = c.IsAggregable,
                    isSystem = c.IsSystem
                }).ToArray(),
                resultColumns = resultSchema.Select(c => new
                {
                    name = c.Name,
                    dataType = c.DataType.ToString().ToLowerInvariant(),
                    isAggregable = c.IsAggregable
                }).ToArray(),
                errors = prepared.ValidationErrors,
                hints = ok ? Array.Empty<string>() : BuildRemediationHints(queryText, prepared.ValidationErrors)
            });
        }
        catch (AqlValidationException ex)
        {
            return Envelope("aql_validation", new
            {
                queryText,
                ok = false,
                errors = ex.Errors,
                hints = BuildRemediationHints(queryText, ex.Errors)
            });
        }
        catch (Exception ex)
        {
            var errors = new[] { $"Parse error: {ex.Message}" };
            return Envelope("aql_validation", new
            {
                queryText,
                ok = false,
                errors,
                hints = BuildRemediationHints(queryText, errors)
            });
        }
    }

    // When validation fails, scan the original query text and error messages
    // for the patterns LLMs most often produce, and return actionable fixes.
    // These are hints, not authoritative — the errors array stays the source
    // of truth. Each hint should point at a specific correction the model
    // can apply mechanically (e.g. "replace X with Y"), not generic advice.
    private static IReadOnlyList<string> BuildRemediationHints(
        string queryText, IReadOnlyList<string> errors)
    {
        var hints = new List<string>();

        // Quoted date keywords — the canonical failure this skill was tuned to
        // catch (BETWEEN(StartDate, "2w ago", "now") returned zero rows).
        bool quotedNow = ContainsQuoted(queryText, "now")
                      || ContainsQuoted(queryText, "today")
                      || ContainsQuoted(queryText, "yesterday")
                      || ContainsQuoted(queryText, "tomorrow");
        bool quotedRelativeDate = Regex.IsMatch(
            queryText,
            "\"\\s*[+\\-]?\\d+\\s*[hdwmyHDWMY]\\s*(ago)?\\s*\"",
            RegexOptions.IgnoreCase);
        if (quotedNow || quotedRelativeDate)
        {
            hints.Add(
                "Date values are NOT strings. Drop the quotes: write NOW instead of \"now\", " +
                "2w ago instead of \"2w ago\", and -7d instead of \"-7d\". " +
                "Example fix: BETWEEN(StartDate, \"2w ago\", \"now\") → BETWEEN(StartDate, 2w ago, NOW).");
        }

        // ISO date string in a place AQL won't accept it.
        if (Regex.IsMatch(queryText, "\"\\d{4}-\\d{2}-\\d{2}"))
        {
            hints.Add(
                "ISO date strings (\"2026-05-01\") are not accepted on date columns. " +
                "Express the window relative to now: e.g. StartDate >= -3w, or " +
                "BETWEEN(StartDate, 3w ago, 1w ago).");
        }

        // Infix date arithmetic — `now - 7d` is the second-most-common mistake.
        if (Regex.IsMatch(queryText, @"\bnow\s*[+\-]\s*\d+\s*[hdwmy]\b", RegexOptions.IgnoreCase))
        {
            hints.Add(
                "AQL has no infix date arithmetic and no `now` keyword in expressions. " +
                "Replace `now - 7d` with `-7d` (or `7d ago`), and bare `now` with the NOW value literal.");
        }

        // SQL leakage.
        if (Regex.IsMatch(queryText, @"\bSELECT\b", RegexOptions.IgnoreCase))
        {
            hints.Add(
                "AQL is not SQL. Use COLUMNS(col1, col2) for projection, not SELECT. " +
                "Clause order is FROM → WHERE → ORDER BY → COLUMNS → GROUP → LIMIT.");
        }

        // ORDER BY referencing a COLUMNS alias — the #1 cause of multi-turn
        // stalling on dashboard chart queries. Clauses are parsed FROM →
        // WHERE → ORDER BY → COLUMNS → GROUP → LIMIT, so an alias defined
        // in COLUMNS(...AS X) isn't visible to ORDER BY. The validator
        // reports "Unknown field 'X'" but the agent often guesses the fix
        // wrong (trying ungrouped base columns, swapping clause order
        // repeatedly). When we can detect this exact pattern, give a
        // mechanical rewrite instruction with the literal field name.
        var unknownFieldErrors = errors
            .Where(e => e.Contains("Unknown field", StringComparison.OrdinalIgnoreCase))
            .ToList();
        bool emittedAliasHint = false;
        if (unknownFieldErrors.Count > 0)
        {
            var aliases = ExtractColumnsAliases(queryText);
            foreach (var err in unknownFieldErrors)
            {
                var nameMatch = Regex.Match(err, @"Unknown field [`'""]?(\w+)[`'""]?", RegexOptions.IgnoreCase);
                if (!nameMatch.Success) continue;
                var name = nameMatch.Groups[1].Value;
                var aliasHit = aliases.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                if (aliasHit is null) continue;
                hints.Add(
                    $"`{aliasHit}` is defined as an alias inside COLUMNS(... AS {aliasHit}), but this entity's " +
                    "ORDER BY resolves only against the source schema — it does not see COLUMNS aliases. " +
                    "(Dataset queries DO support ORDER BY-by-alias; this entity does not.) " +
                    "Fix: repeat the underlying expression in ORDER BY instead of referencing the alias. " +
                    $"Example: `ORDER BY {aliasHit} DESC ... COLUMNS(... AVG(field) AS {aliasHit} ...)` → " +
                    $"`ORDER BY AVG(field) DESC ... COLUMNS(... AVG(field) AS {aliasHit} ...)`.");
                emittedAliasHint = true;
                break; // One targeted alias hint is enough; agent retries with this fix.
            }
        }

        // Generic "unknown field" advice — skip when we already gave the
        // more specific alias-in-ORDER-BY hint, so the agent isn't pulled
        // two directions at once.
        if (!emittedAliasHint && unknownFieldErrors.Count > 0)
        {
            hints.Add(
                "Call describe_aql_entity with the FROM entity to see the exact column list " +
                "(case-insensitive). Field names are not free-form — only the listed columns work.");
        }
        if (errors.Any(e => e.Contains("Unknown entity", StringComparison.OrdinalIgnoreCase)))
        {
            hints.Add(
                "Call get_aql_grammar to see the full list of queryable entity names, then use one of them after FROM.");
        }

        return hints;
    }

    // Extract the alias names from a COLUMNS(...) clause. Used by the
    // alias-in-ORDER-BY hint above. Hand-rolled paren matching because the
    // body can contain nested aggregate calls like AVG(temperature) AS
    // avg_temp, and balanced-paren regex isn't supported in .NET.
    private static IReadOnlyList<string> ExtractColumnsAliases(string queryText)
    {
        var match = Regex.Match(queryText, @"\bCOLUMNS\s*\(", RegexOptions.IgnoreCase);
        if (!match.Success) return Array.Empty<string>();
        int i = match.Index + match.Length;
        int depth = 1;
        int start = i;
        while (i < queryText.Length && depth > 0)
        {
            var ch = queryText[i];
            if (ch == '(') depth++;
            else if (ch == ')') { depth--; if (depth == 0) break; }
            i++;
        }
        if (depth != 0) return Array.Empty<string>();
        var body = queryText.Substring(start, i - start);
        var aliases = new List<string>();
        foreach (Match m in Regex.Matches(body, @"\bAS\s+(\w+)\b", RegexOptions.IgnoreCase))
            aliases.Add(m.Groups[1].Value);
        return aliases;
    }

    private static bool ContainsQuoted(string source, string keyword) =>
        source.Contains($"\"{keyword}\"", StringComparison.OrdinalIgnoreCase);

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
