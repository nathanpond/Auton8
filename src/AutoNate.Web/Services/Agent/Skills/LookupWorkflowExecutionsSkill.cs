using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Flowable;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only workflow execution diagnostics. Wraps IFlowableClient (the BPMN
// engine) and gates every per-instance read through the existing
// WorkflowExecutionInstanceAuthorizer / WorkflowTaskInstanceAuthorizer so the
// chat never reveals an instance the actor can't already see in the UI. Pairs
// with the future OperateWorkflowExecutionsSkill (Phase 3) for cancel /
// reassign / complete actions.
public sealed class LookupWorkflowExecutionsSkill : IAgentSkill
{
    public string Name => "lookup-workflow-executions";

    public string Description =>
        "Find and inspect running workflow executions and their tasks. Read-only.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupWorkflowExecutionsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "find_execution",
                Description: "Search workflow executions by name / id / process model. Returns up to 25 visible matches.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Free text matched against execution name, id, and process model name." },
                        "status": { "type": "string", "description": "Optional status filter, e.g. Running / Completed / Cancelled / Errored." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 50 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeFindExecutionAsync),

            new AgentTool(
                Name: "get_execution",
                Description: "Fetch a single workflow execution summary by process instance id.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string", "description": "Flowable process instance id." }
                      },
                      "required": ["processInstanceId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetExecutionAsync),

            new AgentTool(
                Name: "list_execution_history",
                Description: "Per-activity history for a process instance, ascending by start time. Powers the History tab in the UI.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" },
                        "take": { "type": "integer", "minimum": 1, "maximum": 200 }
                      },
                      "required": ["processInstanceId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListHistoryAsync),

            new AgentTool(
                Name: "get_execution_variables",
                Description: "Snapshot of every variable on a running process instance. Returns empty if the instance has finished.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" }
                      },
                      "required": ["processInstanceId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetVariablesAsync),

            new AgentTool(
                Name: "list_pending_tasks",
                Description: "List the runtime user tasks currently open on a process instance.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "processInstanceId": { "type": "string" }
                      },
                      "required": ["processInstanceId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListPendingTasksAsync),

            new AgentTool(
                Name: "get_task",
                Description: "Fetch a single workflow task by id.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "taskId": { "type": "string" }
                      },
                      "required": ["taskId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetTaskAsync),

            new AgentTool(
                Name: "list_my_tasks",
                Description: "List workflow tasks assigned to the current user.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListMyTasksAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Workflow executions are running BPMN process instances (Flowable). Use find_execution to discover ids, then get_execution / list_execution_history / list_pending_tasks for detail. Tasks are open user-assignments; list_my_tasks shows what's on the current user's plate.";

    private static async Task<JsonElement> InvokeFindExecutionAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var query = ReadString(args, "query");
        var status = ReadString(args, "status");
        var take = ReadTake(args, 25, 50);

        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var instanceAuthorizer = ResolveAuthorizer(context.Services, EntityKinds.WorkflowExecution);

        var all = await flowable.GetWorkflowExecutionsAsync(ct);
        IEnumerable<Models.WorkflowExecutionSummary> filtered = all;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = query.Trim();
            filtered = filtered.Where(e =>
                (e.Name ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                e.Id.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (e.WorkflowModelName ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            filtered = filtered.Where(e => string.Equals(e.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        var candidates = filtered
            .OrderByDescending(e => e.StartedAtUtc ?? DateTimeOffset.MinValue)
            .Take(take * 2)
            .ToList();

        var visible = new List<Models.WorkflowExecutionSummary>(take);
        foreach (var exec in candidates)
        {
            if (visible.Count >= take) break;
            var allowed = await instanceAuthorizer.ExistsAndAuthorizedAsync(
                authorizer, context.Session.User, Actions.View, exec.Id, ct);
            if (allowed) visible.Add(exec);
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_executions",
            source = "IFlowableClient",
            data = visible.Select(MapExecution).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeGetExecutionAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var id = ReadRequiredString(args, "processInstanceId");
        if (id is null) return Error("get_execution", "processInstanceId is required.");

        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var instanceAuthorizer = ResolveAuthorizer(context.Services, EntityKinds.WorkflowExecution);
        if (!await instanceAuthorizer.ExistsAndAuthorizedAsync(authorizer, context.Session.User, Actions.View, id, ct))
        {
            return Error("get_execution", $"Execution '{id}' not found or not visible to current user.");
        }

        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var instance = await flowable.GetProcessInstanceAsync(id, ct);
        if (instance is null) return Error("get_execution", $"No execution with id '{id}'.");

        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_execution",
            source = "IFlowableClient",
            data = new
            {
                id = instance.Id,
                name = instance.Name,
                processDefinitionId = instance.ProcessDefinitionId,
                activityId = instance.ActivityId,
                startUserId = instance.StartUserId,
                suspended = instance.Suspended
            }
        });
    }

    private static async Task<JsonElement> InvokeListHistoryAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var id = ReadRequiredString(args, "processInstanceId");
        if (id is null) return Error("list_execution_history", "processInstanceId is required.");
        var take = ReadTake(args, 50, 200);

        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var instanceAuthorizer = ResolveAuthorizer(context.Services, EntityKinds.WorkflowExecution);
        if (!await instanceAuthorizer.ExistsAndAuthorizedAsync(authorizer, context.Session.User, Actions.View, id, ct))
        {
            return Error("list_execution_history", $"Execution '{id}' not visible to current user.");
        }

        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var history = await flowable.GetWorkflowExecutionHistoryAsync(id, ct);
        var items = history.Take(take).Select(h => new
        {
            activityId = h.ActivityId,
            activityName = h.ActivityName,
            activityType = h.ActivityType,
            startedAtUtc = h.StartedAtUtc,
            endedAtUtc = h.EndedAtUtc,
            durationMs = h.DurationMs,
            assignee = h.Assignee,
            taskId = h.TaskId,
            deleteReason = h.DeleteReason,
            isErrored = h.IsErrored,
            errorMessage = h.ErrorMessage
        }).ToArray();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_execution_history",
            source = "IFlowableClient",
            data = items
        });
    }

    private static async Task<JsonElement> InvokeGetVariablesAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var id = ReadRequiredString(args, "processInstanceId");
        if (id is null) return Error("get_execution_variables", "processInstanceId is required.");

        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var instanceAuthorizer = ResolveAuthorizer(context.Services, EntityKinds.WorkflowExecution);
        if (!await instanceAuthorizer.ExistsAndAuthorizedAsync(authorizer, context.Session.User, Actions.View, id, ct))
        {
            return Error("get_execution_variables", $"Execution '{id}' not visible to current user.");
        }

        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var variables = await flowable.GetProcessInstanceVariablesAsync(id, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_execution_variables",
            source = "IFlowableClient",
            data = variables
        });
    }

    private static async Task<JsonElement> InvokeListPendingTasksAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var id = ReadRequiredString(args, "processInstanceId");
        if (id is null) return Error("list_pending_tasks", "processInstanceId is required.");

        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var instanceAuthorizer = ResolveAuthorizer(context.Services, EntityKinds.WorkflowExecution);
        if (!await instanceAuthorizer.ExistsAndAuthorizedAsync(authorizer, context.Session.User, Actions.View, id, ct))
        {
            return Error("list_pending_tasks", $"Execution '{id}' not visible to current user.");
        }

        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var tasks = await flowable.GetTasksByProcessInstanceAsync(id, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_tasks",
            source = "IFlowableClient",
            data = tasks.Select(MapTask).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeGetTaskAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var taskId = ReadRequiredString(args, "taskId");
        if (taskId is null) return Error("get_task", "taskId is required.");

        var authorizer = context.Services.GetRequiredService<IAuthorizer>();
        var taskAuthorizer = ResolveAuthorizer(context.Services, EntityKinds.WorkflowTask);
        if (!await taskAuthorizer.ExistsAndAuthorizedAsync(authorizer, context.Session.User, Actions.View, taskId, ct))
        {
            return Error("get_task", $"Task '{taskId}' not visible to current user.");
        }

        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var task = await flowable.GetTaskAsync(taskId, ct);
        if (task is null) return Error("get_task", $"No task with id '{taskId}'.");

        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_task",
            source = "IFlowableClient",
            data = MapTask(task)
        });
    }

    private static async Task<JsonElement> InvokeListMyTasksAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var take = ReadTake(args, 25, 100);
        var flowable = context.Services.GetRequiredService<IFlowableClient>();
        var actorId = context.Session.UserId.ToString();
        var list = await flowable.GetTasksAssignedToUserAsync(actorId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "my_workflow_tasks",
            source = "IFlowableClient",
            data = list.Take(take).Select(MapTask).ToArray()
        });
    }

    private static IInstanceAuthorizer ResolveAuthorizer(IServiceProvider services, string kind)
    {
        var authorizers = services.GetServices<IInstanceAuthorizer>();
        return authorizers.First(a => string.Equals(a.Kind, kind, StringComparison.Ordinal));
    }

    private static object MapExecution(Models.WorkflowExecutionSummary e) => new
    {
        id = e.Id,
        name = e.Name,
        processDefinitionId = e.ProcessDefinitionId,
        workflowModelName = e.WorkflowModelName,
        status = e.Status,
        currentStep = e.CurrentStep,
        startUserId = e.StartUserId,
        startedAtUtc = e.StartedAtUtc,
        lastActivityAtUtc = e.LastActivityAtUtc
    };

    private static object MapTask(Models.FlowableTaskSummary t) => new
    {
        id = t.Id,
        name = t.Name,
        taskDefinitionKey = t.TaskDefinitionKey,
        processInstanceId = t.ProcessInstanceId,
        processInstanceName = t.ProcessInstanceName,
        processDefinitionId = t.ProcessDefinitionId,
        processDefinitionName = t.ProcessDefinitionName,
        assignee = t.Assignee,
        createdAtUtc = t.CreatedAtUtc,
        dueDate = t.DueDate
    };

    private static int ReadTake(JsonElement args, int defaultValue, int max) =>
        args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, max)
            : defaultValue;

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? ReadRequiredString(JsonElement args, string name) =>
        ReadString(args, name) is { Length: > 0 } s ? s : null;

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
