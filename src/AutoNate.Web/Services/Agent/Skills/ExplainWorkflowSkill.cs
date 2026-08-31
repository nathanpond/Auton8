using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only diagnostic skill. Two tools: find_workflow narrows by name or
// process key; explain_workflow returns the BPMN XML so the model can describe
// the flow. We deliberately don't pre-summarise the BPMN here — Claude and
// GPT-4-class models read BPMN cleanly, and a structured walker is its own
// project.
//
// IWorkflowModelStore does not gate by actor, so both tools authorize
// explicitly and mirror the HTTP routes over the same store exactly (#19):
// GET /api/workflows/ is RequireKindPermission(WorkflowModel, View), and
// GET /api/workflows/{id} is RequirePermission(WorkflowModel, View, "id").
// Without this, asking the chatbot to explain a workflow returned the full
// BPMN — service-task endpoints and behaviour keys included — to a user whom
// the REST API answers with 403.
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

        if (!await CanViewAnyAsync(context, ct))
        {
            return Error("find_workflow", "WorkflowModel:view permission required.");
        }

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

        // Authorize before the read, and answer a denial and a miss the same
        // way, so the tool cannot be used to probe which workflow ids exist.
        if (!await CanViewAsync(context, workflowId, ct))
        {
            return Error("explain_workflow", $"No workflow with id {workflowId} is visible.");
        }

        var store = context.Services.GetRequiredService<IWorkflowModelStore>();
        var model = await store.GetAsync(workflowId, ct);
        if (model is null)
        {
            return Error("explain_workflow", $"No workflow with id {workflowId} is visible.");
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

    // Kind-level: "may this actor see workflow models at all", the same gate
    // GET /api/workflows/ applies.
    private static async Task<bool> CanViewAnyAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.WorkflowModel, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<bool> CanViewAsync(AgentToolContext ctx, Guid workflowId, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View,
            new EntityRef(EntityKinds.WorkflowModel, workflowId.ToString()), ct);
        return decision.IsAllowed;
    }

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "error",
            source,
            data = new { message }
        });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
