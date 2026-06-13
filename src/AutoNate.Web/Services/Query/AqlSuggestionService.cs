using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;
using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Services.Query;

// Phase 8c — natural-language → AQL suggestion. A focused, one-shot
// counterpart to the agent's AqlAssistSkill: given a plain-English
// description, draft a single AQL query, validate it server-side, and (on
// failure) retry once feeding the validator's errors back to the model.
// Used by the binding "suggest a query" dialog so a non-AQL user can author
// an aql-table binding by describing what they want.
//
// Reuses the agent's configured LLM provider (resolved the same way the
// chat loop does) and the live AQL grammar/entity catalog + validator — so
// suggestions can't drift from what the executor will actually accept.

public sealed record AqlSuggestion(
    string Query,
    bool Valid,
    IReadOnlyList<string> Errors,
    string? Explanation);

public sealed class AqlSuggestionUnavailableException(string message) : Exception(message);

public interface IAqlSuggestionService
{
    Task<AqlSuggestion> SuggestAsync(string description, ClaimsPrincipal user, CancellationToken ct);
}

public sealed class AqlSuggestionService(
    IChatProviderResolver providerResolver,
    IQueryEntityRegistry registry) : IAqlSuggestionService
{
    public async Task<AqlSuggestion> SuggestAsync(
        string description, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new AqlSuggestionUnavailableException("Describe what you want the query to return.");
        }

        var provider =
            await providerResolver.ResolveDefaultForKindAsync("LlmProvider:Anthropic", ct)
            ?? await providerResolver.ResolveDefaultForKindAsync("LlmProvider:OpenAI", ct);
        if (provider is null)
        {
            throw new AqlSuggestionUnavailableException(
                "No AI provider is configured. Ask an admin to set up an LLM connection.");
        }

        var system = await BuildSystemPromptAsync(ct);

        // First draft.
        var raw = await CompleteAsync(provider, system, BuildUserPrompt(description, null, null), ct);
        var (query, explanation) = ParseResponse(raw);
        var (valid, errors) = await ValidateAsync(query, ct);

        // One corrective retry: hand the model its failed query + the errors.
        if (!valid && !string.IsNullOrWhiteSpace(query))
        {
            var raw2 = await CompleteAsync(
                provider, system, BuildUserPrompt(description, query, errors), ct);
            var (query2, explanation2) = ParseResponse(raw2);
            var (valid2, errors2) = await ValidateAsync(query2, ct);
            // Prefer the retry when it's valid, or when the first never parsed.
            if (valid2 || string.IsNullOrWhiteSpace(query))
            {
                return new AqlSuggestion(query2, valid2, errors2, explanation2);
            }
            // Otherwise keep whichever we have; the first draft + its errors.
        }

        return new AqlSuggestion(query, valid, errors, explanation);
    }

    private static async Task<string> CompleteAsync(
        IChatProvider provider, string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var request = new ChatRequest(
            Messages: messages,
            SystemPrompt: system,
            Tools: Array.Empty<ChatTool>(),
            ModelId: provider.ModelId,
            MaxTokens: 1024,
            Temperature: 0.1);

        var sb = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(request, ct))
        {
            switch (chunk)
            {
                case ChatStreamChunk.TextDelta td:
                    sb.Append(td.Delta);
                    break;
                case ChatStreamChunk.Error err:
                    throw new AqlSuggestionUnavailableException(err.Message);
                case ChatStreamChunk.MessageStop:
                    return sb.ToString();
            }
        }
        return sb.ToString();
    }

    private static IReadOnlyList<ChatMessage> BuildUserPrompt(
        string description, string? priorQuery, IReadOnlyList<string>? priorErrors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Request:");
        sb.AppendLine(description.Trim());
        if (!string.IsNullOrWhiteSpace(priorQuery) && priorErrors is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Your previous attempt failed validation:");
            sb.AppendLine($"  query: {priorQuery}");
            sb.AppendLine($"  errors: {string.Join("; ", priorErrors)}");
            sb.AppendLine("Return a corrected query that fixes these errors.");
        }
        return new[]
        {
            new ChatMessage(ChatRole.User, new ChatContentBlock[]
            {
                new ChatContentBlock.TextBlock(sb.ToString())
            })
        };
    }

    private async Task<string> BuildSystemPromptAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "You convert a plain-English request into a single AQL (AutoNate Query Language) query.");
        sb.AppendLine(
            "Respond with ONLY a JSON object — no prose, no markdown fences: " +
            "{\"query\": \"<the AQL query>\", \"explanation\": \"<one short sentence>\"}.");
        sb.AppendLine();
        sb.AppendLine("AQL grammar:");
        sb.AppendLine(
            "  Shape: FROM <Entity>[(\"<arg>\")] [WHERE <expr>] [ORDER BY <col> [ASC|DESC]] " +
            "[COLUMNS(<c1>, ...)] [GROUP(<col>, ...)] [LIMIT <n>]. Clauses MUST appear in this exact order.");
        sb.AppendLine(
            "  Most entities are bare (FROM Records); a few take a string-literal argument that names " +
            "a concrete surface (e.g. FROM Dataset(\"sales\") queries the dataset named 'sales'). The " +
            "list below marks parameterized entities with their argument hint.");
        sb.AppendLine(
            "  ORDER BY (when present) comes BEFORE COLUMNS, and you CANNOT ORDER BY an aggregate " +
            "alias defined in COLUMNS — a grouped count is just COLUMNS(...) GROUP(...) with no ORDER BY.");
        sb.AppendLine("  Projection is COLUMNS(...), never SELECT. String literals use double quotes.");
        sb.AppendLine("  Boolean logic: AND / OR. Aggregates: COUNT(), MIN/MAX/AVG/MEDIAN(col).");
        sb.AppendLine("  Common WHERE functions: CONTAINS(col, \"text\"), BETWEEN(col, lo, hi), IN(col, ...).");
        sb.AppendLine(
            "  DATES are relative to now and are NEVER quoted strings. Three legal forms only: " +
            "-7d (int+unit h/d/w/m/y, negative=past), 2w ago (desugars to a negative offset), and NOW. " +
            "There is NO `now - 7d` arithmetic and NO bare today/yesterday keywords.");
        sb.AppendLine();
        sb.AppendLine("Worked examples:");
        sb.AppendLine("  Records of a type: FROM Records WHERE RecordType = \"Car\" ORDER BY Name");
        sb.AppendLine("  Recent window: FROM Flows WHERE StartDate >= -2w ORDER BY StartDate DESC");
        sb.AppendLine("  Date window: FROM Flows WHERE BETWEEN(StartDate, 2w ago, NOW)");
        sb.AppendLine("  Substring: FROM Records WHERE CONTAINS(Name, \"acme\")");
        sb.AppendLine("  Grouped count: FROM Flows COLUMNS(Status, COUNT() AS Total) GROUP(Status)");
        sb.AppendLine();
        sb.AppendLine("Queryable entities and their columns:");
        foreach (var name in registry.EntityNames)
        {
            if (!registry.TryGet(name, out var entity)) continue;
            var cols = string.Join(", ", entity.StaticSchema.Select(c => c.Name));
            var entityRef = entity.AcceptsEntityArgument
                ? $"{entity.Name}(\"<{entity.EntityArgumentHint ?? "name"}>\")"
                : entity.Name;
            sb.AppendLine($"  FROM {entityRef}: {cols}");
            if (string.Equals(entity.Name, "Records", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(
                    "    (Records also have custom fields that vary by RecordType — filter " +
                    "RecordType = \"<Type>\" and reference custom field names directly.)");
            }

            // Allowed values for enum-like columns (e.g. Flows.Status). Without
            // these the model invents natural phrasings ("In Progress") that are
            // valid AQL but match zero rows. recordType filter is null — we want
            // the entity-wide enums, not per-RecordType custom-field values.
            IReadOnlyDictionary<string, IReadOnlyList<string>> enums;
            try
            {
                enums = await entity.GetDynamicColumnEnumsAsync(null, ct);
            }
            catch
            {
                enums = new Dictionary<string, IReadOnlyList<string>>();
            }
            foreach (var (column, values) in enums)
            {
                if (values.Count == 0) continue;
                sb.AppendLine(
                    $"    {column} values (use these EXACT literals): {string.Join(" | ", values)}");
            }
        }
        sb.AppendLine();
        sb.AppendLine(
            "Only use the entities, columns, and listed value literals above — never invent field, " +
            "function, or value names. When a column has listed values, use the exact literal (e.g. " +
            "Status = \"In-progress\", not \"In Progress\"). If the request is ambiguous, pick the " +
            "most likely entity and a reasonable query.");
        return sb.ToString();
    }

    // Tolerant extraction of the {query, explanation} object from the model's
    // text (handles stray prose or ```json fences). Falls back to treating the
    // whole response as the query so a near-miss still surfaces to the user.
    private static (string query, string? explanation) ParseResponse(string raw)
    {
        var text = raw.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var json = text.Substring(start, end - start + 1);
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var q = root.TryGetProperty("query", out var qe) && qe.ValueKind == JsonValueKind.String
                    ? qe.GetString() ?? string.Empty
                    : string.Empty;
                var ex = root.TryGetProperty("explanation", out var ee) && ee.ValueKind == JsonValueKind.String
                    ? ee.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(q)) return (q.Trim(), ex);
            }
            catch (JsonException)
            {
                // fall through to raw
            }
        }
        return (text, null);
    }

    private async Task<(bool valid, IReadOnlyList<string> errors)> ValidateAsync(
        string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (false, new[] { "No query was produced." });
        }
        try
        {
            var ast = AqlParser.Parse(query);
            var validator = new AqlValidator(registry);
            var prepared = await validator.ValidateAsync(ast, hardCap: 1000, ct);
            return (prepared.ValidationErrors.Count == 0, prepared.ValidationErrors);
        }
        catch (AqlValidationException ex)
        {
            return (false, ex.Errors);
        }
        catch (Exception ex)
        {
            return (false, new[] { $"Parse error: {ex.Message}" });
        }
    }
}
