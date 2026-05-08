using System.Text.Json;
using AutoNate.Web.Services.SystemIssues;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only system-issue diagnostics. The detector/remediator pipeline
// already populates system_issues with structured FactsJson; this skill just
// surfaces those rows so the model can explain them in plain English.
public sealed class AnalyzeSystemIssueSkill : IAgentSkill
{
    public string Name => "analyze-system-issue";

    public string Description => "List open or recent system issues, and read a single issue's facts to explain its likely cause.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public AnalyzeSystemIssueSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_system_issues",
                Description: "List system issues filtered by state and severity. Defaults to open issues.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "state": { "type": "string", "description": "open / acknowledged / resolved. Defaults to open." },
                        "severity": { "type": "string", "description": "low / medium / high / critical. Optional." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100, "description": "Max rows. Default 25." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_system_issue",
                Description: "Fetch one system issue with its facts payload and remediation history.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "issueId": { "type": "string", "description": "GUID of the system issue." }
                      },
                      "required": ["issueId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "When asked to diagnose system health, call list_system_issues first; only call get_system_issue once you've identified a specific row to explain.";

    private static async Task<JsonElement> InvokeListAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        var state = args.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : SystemIssueStates.Open;
        var severity = args.TryGetProperty("severity", out var sv) && sv.ValueKind == JsonValueKind.String
            ? sv.GetString()
            : null;
        var take = args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, 100)
            : 25;

        var store = context.Services.GetRequiredService<ISystemIssueStore>();
        var rows = await store.ListAsync(new SystemIssueListQuery(state, severity, Category: null, Skip: 0, Take: take), ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "system_issues",
            source = "ISystemIssueStore",
            data = rows.Select(i => new
            {
                id = i.Id,
                title = i.Title,
                summary = i.Summary,
                state = i.State,
                severity = i.Severity,
                category = i.Category,
                detectorId = i.DetectorId,
                occurrenceCount = i.OccurrenceCount,
                firstSeenAtUtc = i.FirstSeenAtUtc,
                lastSeenAtUtc = i.LastSeenAtUtc
            }).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        if (!args.TryGetProperty("issueId", out var idProp) || idProp.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idProp.GetString(), out var id))
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "error",
                source = "get_system_issue",
                data = new { message = "issueId is required and must be a GUID." }
            });
        }

        var store = context.Services.GetRequiredService<ISystemIssueStore>();
        var issue = await store.GetAsync(id, ct);
        if (issue is null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "error",
                source = "get_system_issue",
                data = new { message = $"No system issue with id {id}." }
            });
        }

        // Parse FactsJson back into a JsonElement so the model can read the
        // detector-specific payload (counts, error excerpts, ids) directly.
        JsonElement facts;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(issue.FactsJson) ? "{}" : issue.FactsJson);
            facts = doc.RootElement.Clone();
        }
        catch
        {
            using var doc = JsonDocument.Parse("{}");
            facts = doc.RootElement.Clone();
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "system_issue",
            source = "ISystemIssueStore",
            data = new
            {
                id = issue.Id,
                title = issue.Title,
                summary = issue.Summary,
                state = issue.State,
                severity = issue.Severity,
                category = issue.Category,
                detectorId = issue.DetectorId,
                occurrenceCount = issue.OccurrenceCount,
                firstSeenAtUtc = issue.FirstSeenAtUtc,
                lastSeenAtUtc = issue.LastSeenAtUtc,
                relatedEntityKind = issue.RelatedEntityKind,
                relatedEntityId = issue.RelatedEntityId,
                facts,
                autoRemediationAttemptCount = issue.AutoRemediationAttemptCount,
                autoRemediationLastError = issue.AutoRemediationLastError
            }
        });
    }

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
