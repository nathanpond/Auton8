using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 3 workflow-execution operate skill. Every mutating tool gates on the
// matching per-instance authorizer (WorkflowExecution: Cancel / Override /
// Delete; WorkflowTask: Complete / Override) — same authorization the SPA's
// admin override surface enforces. All commits flow through IFlowableClient
// so audits + state stay aligned with what /api/executions/* writes today.
public sealed class OperateWorkflowExecutionsSkill : IAgentSkill
{
    public string Name => "operate-workflow-executions";

    public string Description =>
        "Cancel running workflow executions, reassign tasks, change due dates, and complete tasks (incl. force-complete with override).";

    public IReadOnlyList<AgentTool> Tools { get; }

    public OperateWorkflowExecutionsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "cancel_execution",
                Description: "Stop a running workflow execution. The historic record stays; status flips to Cancelled. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["processInstanceId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCancelAsync),

            new AgentTool(
                Name: "delete_execution",
                Description: "Delete a process instance entirely (runtime + history). Higher-impact than cancel — use cancel for live operations and delete only for cleanup.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["processInstanceId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteAsync),

            new AgentTool(
                Name: "reassign_task",
                Description: "Change the assignee of a runtime user task. Pass assignee=null to unassign.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" },
                        "taskId": { "type": "string" },
                        "assignee": { "type": ["string", "null"], "description": "User id to assign to, or null to unassign." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["processInstanceId", "taskId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeReassignAsync),

            new AgentTool(
                Name: "change_task_due_date",
                Description: "Set or clear (null) the due date on a runtime user task. ISO-8601 string with timezone.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" },
                        "taskId": { "type": "string" },
                        "dueDate": { "type": ["string", "null"], "description": "ISO-8601 datetime, or null to clear." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["processInstanceId", "taskId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDueDateAsync),

            new AgentTool(
                Name: "complete_task",
                Description: "Complete the task as the current user. Use force_complete_task instead when the task isn't assigned to the current user.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "taskId": { "type": "string" },
                        "variables": { "type": ["object", "null"], "description": "Optional process-variable updates submitted with the completion." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["taskId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCompleteAsync),

            new AgentTool(
                Name: "force_complete_task",
                Description: "Complete a task as the current user even when it's assigned to someone else — recorded as an override in the workflow_task_completions table.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" },
                        "taskId": { "type": "string" },
                        "variables": { "type": ["object", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["processInstanceId", "taskId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeForceCompleteAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Workflow operate protocol: " +
        "(1) Use lookup-workflow-executions.find_execution to get the processInstanceId, then get_task or list_pending_tasks for the taskId. " +
        "(2) Always confirm with the user before re-calling with confirmed=true — these actions affect live workflow state. " +
        "(3) Prefer complete_task over force_complete_task; force-complete records an override and should only be used when the task isn't on the current user's plate.";

    // ---- helpers ---------------------------------------------------------

    private static IInstanceAuthorizer Resolve(IServiceProvider services, string kind) =>
        services.GetServices<IInstanceAuthorizer>()
            .First(a => string.Equals(a.Kind, kind, StringComparison.Ordinal));

    private static async Task<bool> CanExecutionAsync(AgentToolContext ctx, string processInstanceId, string action, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var instance = Resolve(ctx.Services, EntityKinds.WorkflowExecution);
        return await instance.ExistsAndAuthorizedAsync(authorizer, ctx.Session.User, action, processInstanceId, ct);
    }

    private static async Task<bool> CanTaskAsync(AgentToolContext ctx, string taskId, string action, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var instance = Resolve(ctx.Services, EntityKinds.WorkflowTask);
        return await instance.ExistsAndAuthorizedAsync(authorizer, ctx.Session.User, action, taskId, ct);
    }

    // ---- cancel_execution -----------------------------------------------

    private static async Task<JsonElement> InvokeCancelAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "cancel_execution";
        var pid = ReadRequiredString(args, "processInstanceId");
        if (pid is null) return ConfirmGate.Rejected(action, "processInstanceId is required.");
        if (!await CanExecutionAsync(ctx, pid, Actions.Cancel, ct))
        {
            return ConfirmGate.Rejected(action, $"Cancel permission required on execution {pid}.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("execution_cancel_proposal", action, new { processInstanceId = pid });
        }
        var flowable = ctx.Services.GetRequiredService<IFlowableClient>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        await flowable.CancelWorkflowExecutionAsync(pid, ct);
        await audit.PublishAsync(
            WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionCancelled,
            WorkflowResourceKinds.Execution,
            resource: new { processInstanceId = pid },
            details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("execution_cancel_committed", action, new { processInstanceId = pid });
    }

    // ---- delete_execution -----------------------------------------------

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_execution";
        var pid = ReadRequiredString(args, "processInstanceId");
        if (pid is null) return ConfirmGate.Rejected(action, "processInstanceId is required.");
        if (!await CanExecutionAsync(ctx, pid, Actions.Delete, ct))
        {
            return ConfirmGate.Rejected(action, $"Delete permission required on execution {pid}.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("execution_delete_proposal", action, new { processInstanceId = pid });
        }
        var flowable = ctx.Services.GetRequiredService<IFlowableClient>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        await flowable.DeleteWorkflowExecutionAsync(pid, ct);
        await audit.PublishAsync(
            WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionDeleted,
            WorkflowResourceKinds.Execution,
            resource: new { processInstanceId = pid },
            details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("execution_delete_committed", action, new { processInstanceId = pid });
    }

    // ---- reassign_task ---------------------------------------------------

    private static async Task<JsonElement> InvokeReassignAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "reassign_task";
        var pid = ReadRequiredString(args, "processInstanceId");
        var taskId = ReadRequiredString(args, "taskId");
        if (pid is null) return ConfirmGate.Rejected(action, "processInstanceId is required.");
        if (taskId is null) return ConfirmGate.Rejected(action, "taskId is required.");
        var assignee = args.TryGetProperty("assignee", out var a)
            && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (!await CanExecutionAsync(ctx, pid, Actions.Override, ct))
        {
            return ConfirmGate.Rejected(action, $"Override permission required on execution {pid}.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("task_reassign_proposal", action, new { processInstanceId = pid, taskId, assignee });
        }
        var flowable = ctx.Services.GetRequiredService<IFlowableClient>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        await flowable.UpdateTaskAssigneeAsync(taskId, assignee, ct);
        await audit.PublishAsync(
            WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskReassigned,
            WorkflowResourceKinds.Task,
            resource: new { processInstanceId = pid, taskId, assignee },
            details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("task_reassign_committed", action, new { processInstanceId = pid, taskId, assignee });
    }

    // ---- change_task_due_date -------------------------------------------

    private static async Task<JsonElement> InvokeDueDateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "change_task_due_date";
        var pid = ReadRequiredString(args, "processInstanceId");
        var taskId = ReadRequiredString(args, "taskId");
        if (pid is null) return ConfirmGate.Rejected(action, "processInstanceId is required.");
        if (taskId is null) return ConfirmGate.Rejected(action, "taskId is required.");
        DateTimeOffset? dueDate = null;
        if (args.TryGetProperty("dueDate", out var d))
        {
            if (d.ValueKind == JsonValueKind.String)
            {
                if (!DateTimeOffset.TryParse(d.GetString(), out var parsed))
                {
                    return ConfirmGate.Rejected(action, "dueDate must be an ISO-8601 datetime or null.");
                }
                dueDate = parsed;
            }
            else if (d.ValueKind != JsonValueKind.Null)
            {
                return ConfirmGate.Rejected(action, "dueDate must be a string or null.");
            }
        }
        if (!await CanExecutionAsync(ctx, pid, Actions.Override, ct))
        {
            return ConfirmGate.Rejected(action, $"Override permission required on execution {pid}.");
        }
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("task_due_date_proposal", action, new { processInstanceId = pid, taskId, dueDate });
        }
        var flowable = ctx.Services.GetRequiredService<IFlowableClient>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        await flowable.UpdateTaskDueDateAsync(taskId, dueDate, ct);
        await audit.PublishAsync(
            WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskDueDateChanged,
            WorkflowResourceKinds.Task,
            resource: new { processInstanceId = pid, taskId, dueDate },
            details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("task_due_date_committed", action, new { processInstanceId = pid, taskId, dueDate });
    }

    // ---- complete_task ---------------------------------------------------

    private static async Task<JsonElement> InvokeCompleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "complete_task";
        var taskId = ReadRequiredString(args, "taskId");
        if (taskId is null) return ConfirmGate.Rejected(action, "taskId is required.");
        if (!await CanTaskAsync(ctx, taskId, Actions.Complete, ct))
        {
            return ConfirmGate.Rejected(action, $"Complete permission required on task {taskId}.");
        }
        var variables = ReadVariables(args, "variables");
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("task_complete_proposal", action, new
            {
                taskId,
                variableCount = variables?.Count ?? 0,
                variableNames = variables?.Keys.ToArray()
            });
        }
        var flowable = ctx.Services.GetRequiredService<IFlowableClient>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var recorder = ctx.Services.GetRequiredService<WorkflowTaskCompletionRecorder>();
        await flowable.CompleteTaskAsync(taskId, variables, ct);
        await recorder.RecordAsync(taskId, ctx.Session.UserId.ToString(), wasOverride: false, ct);
        await audit.PublishAsync(
            WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskCompleted,
            WorkflowResourceKinds.Task,
            resource: new { taskId },
            details: new { source = "chatbot", hadVariables = variables is { Count: > 0 } }, ct);
        return ConfirmGate.Committed("task_complete_committed", action, new { taskId });
    }

    // ---- force_complete_task --------------------------------------------

    private static async Task<JsonElement> InvokeForceCompleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "force_complete_task";
        var pid = ReadRequiredString(args, "processInstanceId");
        var taskId = ReadRequiredString(args, "taskId");
        if (pid is null) return ConfirmGate.Rejected(action, "processInstanceId is required.");
        if (taskId is null) return ConfirmGate.Rejected(action, "taskId is required.");
        if (!await CanExecutionAsync(ctx, pid, Actions.Override, ct))
        {
            return ConfirmGate.Rejected(action, $"Override permission required on execution {pid}.");
        }
        var variables = ReadVariables(args, "variables");
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("task_force_complete_proposal", action, new
            {
                processInstanceId = pid,
                taskId,
                variableCount = variables?.Count ?? 0
            });
        }
        var flowable = ctx.Services.GetRequiredService<IFlowableClient>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var recorder = ctx.Services.GetRequiredService<WorkflowTaskCompletionRecorder>();
        await flowable.CompleteTaskAsync(taskId, variables, ct);
        await recorder.RecordAsync(taskId, ctx.Session.UserId.ToString(), wasOverride: true, ct);
        await audit.PublishAsync(
            WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskForceCompleted,
            WorkflowResourceKinds.Task,
            resource: new { processInstanceId = pid, taskId },
            details: new { source = "chatbot", hadVariables = variables is { Count: > 0 } }, ct);
        return ConfirmGate.Committed("task_force_complete_committed", action, new { processInstanceId = pid, taskId });
    }

    // ---- args helpers ----------------------------------------------------

    private static IReadOnlyDictionary<string, object?>? ReadVariables(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object) return null;
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in v.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var i) ? i : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText()
            };
        }
        return dict;
    }

    private static string? ReadRequiredString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
