using System.Text.Json;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only diagnostic skill. Two tools: find_workflow narrows by name or
// process key; explain_workflow returns the BPMN XML so the model can describe
// the flow. We deliberately don't pre-summarise the BPMN here — Claude and
// GPT-4-class models read BPMN cleanly, and a structured walker is its own
// project.
public sealed class ExplainWorkflowSkill : IAgentSkill
{
    public string Name => "explain-workflow";

    public string Description => "Look up workflow models by name or process key, and explain how a workflow works by reading its BPMN XML.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ExplainWorkflowSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "find_workflow",
                Description: "Search workflow models by name or process key. Returns up to 25 matches.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Free-text match against name or process key. Empty = first 25 by recent." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeFindWorkflowAsync),

            new AgentTool(
                Name: "explain_workflow",
                Description: "Fetch the workflow model for a given workflow id. Returns name, process key, and full BPMN XML.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "workflowId": { "type": "string", "description": "GUID of the workflow model." }
                      },
                      "required": ["workflowId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeExplainWorkflowAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "When asked about a specific workflow, prefer find_workflow first; only call explain_workflow once you have a confirmed workflow id.";

    private static async Task<JsonElement> InvokeFindWorkflowAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        var query = args.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? string.Empty
            : string.Empty;

        var store = context.Services.GetRequiredService<IWorkflowModelStore>();
        var all = await store.ListAsync(ct);
        var filtered = string.IsNullOrWhiteSpace(query)
            ? all.Take(25)
            : all.Where(m =>
                (m.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.ProcessKey?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).Take(25);

        var summary = new
        {
            kind = "workflow_search_results",
            source = "IWorkflowModelStore",
            data = filtered.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                processKey = m.ProcessKey,
                isDraft = m.IsDraft,
                publishedVersionNumber = m.PublishedVersionNumber,
                updatedAtUtc = m.UpdatedAtUtc
            }).ToArray()
        };

        return JsonSerializer.SerializeToElement(summary);
    }

    private static async Task<JsonElement> InvokeExplainWorkflowAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        if (!args.TryGetProperty("workflowId", out var idProp) || idProp.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idProp.GetString(), out var workflowId))
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "error",
                source = "explain_workflow",
                data = new { message = "workflowId is required and must be a GUID." }
            });
        }

        var store = context.Services.GetRequiredService<IWorkflowModelStore>();
        var model = await store.GetAsync(workflowId, ct);
        if (model is null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "error",
                source = "explain_workflow",
                data = new { message = $"No workflow with id {workflowId} is visible." }
            });
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_model",
            source = "IWorkflowModelStore",
            data = new
            {
                id = model.Id,
                name = model.Name,
                processKey = model.ProcessKey,
                isDraft = model.IsDraft,
                publishedVersionNumber = model.PublishedVersionNumber,
                bpmnXml = model.BpmnXml
            }
        });
    }

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
