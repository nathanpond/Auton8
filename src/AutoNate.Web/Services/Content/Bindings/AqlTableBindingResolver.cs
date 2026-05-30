using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Query;

namespace AutoNate.Web.Services.Content.Bindings;

// AQL table binding — runs an AQL query under the calling principal's
// permissions and stores the QueryResult shape (columns + rows +
// counts) as the resolved value.
//
// Config shape:
//   { queryText: string, limit?: int }
//
// Resolved value shape:
//   { columns: [{ name, dataType }], rows: [...], totalCount, truncated,
//     durationMs }
//
// Permission model: IAqlExecutor already enforces per-row + per-entity
// authorization. Two users with different grants will see different
// row sets in the same binding — `LastResolvedByUserId` (set by the
// endpoint) records whose view produced the stored snapshot so
// downstream readers know what they're looking at.
public sealed class AqlTableBindingResolver : IDocumentBindingResolver
{
    private readonly IAqlExecutor _aql;

    // Default cap on rows persisted into the snapshot. AQL itself
    // imposes a hard cap; this one applies on top so a binding doesn't
    // bloat `last_resolved_value_jsonb` with thousands of rows. Caller
    // can pass a smaller `limit` in the config; can't exceed this.
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    public AqlTableBindingResolver(IAqlExecutor aql)
    {
        _aql = aql;
    }

    public string Kind => DocumentBindingKinds.AqlTable;

    public async Task<DocumentBindingResolveResult> ResolveAsync(
        string configJsonb,
        ClaimsPrincipal actor,
        CancellationToken ct)
    {
        var config = ParseConfig(configJsonb);
        var cap = Math.Clamp(config.Limit ?? DefaultLimit, 1, MaxLimit);

        QueryResult result;
        try
        {
            result = await _aql.ExecuteAsync(config.QueryText, actor, cap, ct);
        }
        catch (AqlValidationException ex)
        {
            // Bad query is a caller-facing error — the user wrote
            // something the validator rejected. Surface the message
            // verbatim so the SPA can show it in the insert dialog.
            throw new DocumentBindingResolveException(
                $"AQL validation failed: {ex.Message}", statusCode: 400);
        }

        var resolvedJson = JsonSerializer.Serialize(new
        {
            columns = result.Columns.Select(c => new
            {
                name = c.Name,
                dataType = c.DataType.ToString().ToLowerInvariant()
            }),
            rows = result.Rows,
            totalCount = result.TotalCount,
            truncated = result.Truncated,
            durationMs = result.DurationMs
        });

        // Label suggestion is just the first ~40 chars of the query — a
        // human-readable handle the user can override.
        var label = config.QueryText.Length > 40
            ? config.QueryText.AsSpan(0, 40).ToString() + "…"
            : config.QueryText;

        return new DocumentBindingResolveResult(resolvedJson, label);
    }

    private static AqlTableBindingConfig ParseConfig(string configJsonb)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AqlTableBindingConfig>(
                configJsonb,
                ConfigJsonOpts);
            if (parsed is null) throw new DocumentBindingResolveException(
                "aql-table config is empty.");
            if (string.IsNullOrWhiteSpace(parsed.QueryText)) throw new DocumentBindingResolveException(
                "aql-table config: queryText is required.");
            return parsed;
        }
        catch (JsonException ex)
        {
            throw new DocumentBindingResolveException(
                $"aql-table config: malformed JSON ({ex.Message}).");
        }
    }

    private static readonly JsonSerializerOptions ConfigJsonOpts =
        new(JsonSerializerDefaults.Web);

    private sealed record AqlTableBindingConfig(string QueryText, int? Limit);
}
