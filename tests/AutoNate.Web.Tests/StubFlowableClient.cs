using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Tests;

/// <summary>
/// Test double for IFlowableClient. Returns canned responses and records
/// invocations so endpoint tests can verify wiring without a real Flowable
/// server. Not for unit testing the FlowableClient itself.
/// </summary>
internal sealed class StubFlowableClient : IFlowableClient
{
    public List<string> Calls { get; } = new();

    // Tests configure these to seed canned responses for the methods that
    // authorization handlers consult.
    public Dictionary<string, FlowableProcessInstanceSummary> InstancesById { get; } = new();
    public Dictionary<string, List<FlowableTaskSummary>> TasksByUser { get; } = new();

    public Task<WorkflowDeploymentInfo> DeployProcessAsync(
        WorkflowModel model, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Deploy:{model.ProcessKey}");
        return Task.FromResult(new WorkflowDeploymentInfo
        {
            DeploymentId = "stub-deployment",
            ProcessDefinitionId = "stub-pd",
            ProcessDefinitionKey = model.ProcessKey,
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });
    }

    // Tests can seed this to drive both GetLatestProcessDefinitionAsync and
    // the bulk variant. Keyed by processDefinitionKey.
    public Dictionary<string, FlowableProcessDefinitionSummary> ProcessDefinitionsByKey { get; } = new();

    public Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(
        string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetLatest:{processDefinitionKey}");
        ProcessDefinitionsByKey.TryGetValue(processDefinitionKey, out var summary);
        return Task.FromResult<FlowableProcessDefinitionSummary?>(summary);
    }

    public Task<IReadOnlyList<FlowableProcessDefinitionSummary>> GetLatestProcessDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        Calls.Add("ListLatestDefinitions");
        IReadOnlyList<FlowableProcessDefinitionSummary> list = ProcessDefinitionsByKey.Values.ToArray();
        return Task.FromResult(list);
    }

    public Task SuspendProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"SuspendDefinition:{processDefinitionKey}");
        if (ProcessDefinitionsByKey.TryGetValue(processDefinitionKey, out var existing))
        {
            ProcessDefinitionsByKey[processDefinitionKey] = existing with { Suspended = true };
        }
        return Task.CompletedTask;
    }

    public Task ActivateProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"ActivateDefinition:{processDefinitionKey}");
        if (ProcessDefinitionsByKey.TryGetValue(processDefinitionKey, out var existing))
        {
            ProcessDefinitionsByKey[processDefinitionKey] = existing with { Suspended = false };
        }
        return Task.CompletedTask;
    }

    public List<(string ProcessDefinitionKey, string? Name, IReadOnlyDictionary<string, object?>? Variables)>
        StartedProcesses { get; } = new();

    // Set to make StartProcessInstanceAsync throw — dispatcher tests for the
    // per-process error-isolation path use this without needing a separate
    // IFlowableClient implementation.
    public Exception? StartProcessInstanceThrows { get; set; }

    public Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(
        string processDefinitionKey,
        string? name = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Start:{processDefinitionKey}:{name ?? "(unnamed)"}");
        StartedProcesses.Add((processDefinitionKey, name, variables));
        if (StartProcessInstanceThrows is not null)
        {
            throw StartProcessInstanceThrows;
        }
        return Task.FromResult(new FlowableProcessInstanceSummary { Name = name });
    }

    // Tests can seed this to assert the count-based auto-naming flow.
    public Dictionary<string, int> InstanceCountsByDefinitionKey { get; } = new();

    public Task<int> GetHistoricProcessInstanceCountByDefinitionKeyAsync(
        string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"CountByDefinitionKey:{processDefinitionKey}");
        InstanceCountsByDefinitionKey.TryGetValue(processDefinitionKey, out var count);
        return Task.FromResult(count);
    }

    public Task<FlowableProcessInstanceSummary?> GetProcessInstanceAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetInstance:{processInstanceId}");
        InstancesById.TryGetValue(processInstanceId, out var summary);
        return Task.FromResult(summary);
    }

    // Tests that exercise the workflow detectors set this list to control
    // what GetWorkflowExecutionsAsync returns. Default is empty (existing
    // tests rely on the no-op shape).
    public List<WorkflowExecutionSummary> Executions { get; } = new();

    public Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(
        CancellationToken cancellationToken = default)
    {
        Calls.Add("ListExecutions");
        return Task.FromResult<IReadOnlyList<WorkflowExecutionSummary>>(Executions.ToArray());
    }

    public Task<WorkflowExecutionDiagramDetail> GetWorkflowExecutionDiagramDetailAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Diagram:{processInstanceId}");
        return Task.FromResult(new WorkflowExecutionDiagramDetail());
    }

    // Tests can seed this to drive the history endpoint response. Defaults to
    // an empty list when not set.
    public Dictionary<string, List<WorkflowExecutionHistoryEvent>> HistoryByInstance { get; } = new();

    public Task<IReadOnlyList<WorkflowExecutionHistoryEvent>> GetWorkflowExecutionHistoryAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"History:{processInstanceId}");
        HistoryByInstance.TryGetValue(processInstanceId, out var events);
        IReadOnlyList<WorkflowExecutionHistoryEvent> list =
            events?.AsReadOnly() ?? (IReadOnlyList<WorkflowExecutionHistoryEvent>)Array.Empty<WorkflowExecutionHistoryEvent>();
        return Task.FromResult(list);
    }

    public Dictionary<string, List<WorkflowExecutionLogEntry>> LogByInstance { get; } = new();

    public Task<IReadOnlyList<WorkflowExecutionLogEntry>> GetWorkflowExecutionLogAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Log:{processInstanceId}");
        LogByInstance.TryGetValue(processInstanceId, out var entries);
        IReadOnlyList<WorkflowExecutionLogEntry> list =
            entries?.AsReadOnly() ?? (IReadOnlyList<WorkflowExecutionLogEntry>)Array.Empty<WorkflowExecutionLogEntry>();
        return Task.FromResult(list);
    }

    public Task DeleteWorkflowExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"DeleteExecution:{processInstanceId}");
        return Task.CompletedTask;
    }

    // Tests can seed this to control how many instances the bulk-delete
    // endpoint reports back; the stub doesn't enumerate real instances.
    public int DeleteAllWorkflowExecutionsResult { get; set; }

    public Task<int> DeleteAllWorkflowExecutionsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("DeleteAllExecutions");
        return Task.FromResult(DeleteAllWorkflowExecutionsResult);
    }

    public Task CancelWorkflowExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"CancelExecution:{processInstanceId}");
        return Task.CompletedTask;
    }

    // Tests that need GetTasksByProcessInstanceAsync to return tasks
    // populate this dictionary keyed by process instance id. Default empty.
    public Dictionary<string, List<FlowableTaskSummary>> TasksByProcess { get; } = new();

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksByInstance:{processInstanceId}");
        TasksByProcess.TryGetValue(processInstanceId, out var tasks);
        return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(
            (IReadOnlyList<FlowableTaskSummary>?)tasks ?? Array.Empty<FlowableTaskSummary>());
    }

    // Tests that exercise the projection-framework task polling feed populate
    // RuntimeTasks; the projection upserts each entry into workflow_task_cache.
    public List<FlowableTaskSummary> RuntimeTasks { get; } = new();

    public Task<IReadOnlyList<FlowableTaskSummary>> GetRuntimeTasksAsync(
        int start, int size, CancellationToken cancellationToken = default)
    {
        Calls.Add($"RuntimeTasks:start={start},size={size}");
        if (start >= RuntimeTasks.Count) return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(Array.Empty<FlowableTaskSummary>());
        var page = RuntimeTasks.Skip(start).Take(size).ToArray();
        return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(page);
    }

    // Tests for the history projection seed this list; the global page method
    // returns entries filtered by sinceUtc and paged by start/size.
    public List<FlowableHistoricActivityEvent> HistoricActivityEvents { get; } = new();

    public Task<IReadOnlyList<FlowableHistoricActivityEvent>> GetHistoricActivityEventsAsync(
        int start, int size, DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"HistoricActivities:start={start},size={size},since={sinceUtc?.UtcDateTime.ToString("o") ?? "<none>"}");
        var src = sinceUtc is { } since
            ? HistoricActivityEvents.Where(e => e.StartTime is { } st && st >= since)
            : HistoricActivityEvents;
        var page = src.OrderBy(e => e.StartTime ?? DateTimeOffset.MinValue).Skip(start).Take(size).ToArray();
        return Task.FromResult<IReadOnlyList<FlowableHistoricActivityEvent>>(page);
    }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUserAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksForUser:{userId}");
        TasksByUser.TryGetValue(userId, out var tasks);
        IReadOnlyList<FlowableTaskSummary> list = tasks?.AsReadOnly() ?? (IReadOnlyList<FlowableTaskSummary>)Array.Empty<FlowableTaskSummary>();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksForUsers:{string.Join(",", userIds)}");
        var merged = new Dictionary<string, FlowableTaskSummary>(StringComparer.Ordinal);
        foreach (var userId in userIds.Distinct(StringComparer.Ordinal))
        {
            if (!TasksByUser.TryGetValue(userId, out var tasks)) continue;
            foreach (var t in tasks)
            {
                merged.TryAdd(t.Id, t);
            }
        }
        IReadOnlyList<FlowableTaskSummary> list = merged.Values.ToArray();
        return Task.FromResult(list);
    }

    public Task CompleteTaskAsync(
        string taskId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CompleteTask:{taskId}");
        return Task.CompletedTask;
    }

    public Dictionary<string, string?> TaskAssigneesByTaskId { get; } = new();

    public Task UpdateTaskAssigneeAsync(
        string taskId,
        string? assignee,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"UpdateTaskAssignee:{taskId}:{assignee ?? "(null)"}");
        TaskAssigneesByTaskId[taskId] = assignee;
        return Task.CompletedTask;
    }

    public Dictionary<string, DateTimeOffset?> TaskDueDatesByTaskId { get; } = new();

    public Task UpdateTaskDueDateAsync(
        string taskId,
        DateTimeOffset? dueDate,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"UpdateTaskDueDate:{taskId}:{dueDate?.ToString("O") ?? "(null)"}");
        TaskDueDatesByTaskId[taskId] = dueDate;
        return Task.CompletedTask;
    }

    public Dictionary<string, List<ProcessVariableUpdate>> VariableUpdatesByInstance { get; } = new();

    public Dictionary<(string ProcessInstanceId, string ActivityId), List<string>> CompletedAssigneesByActivity { get; } = new();

    public List<(string SignalName, IReadOnlyDictionary<string, object?>? Variables)> BroadcastedSignals { get; } = new();

    // Set to make BroadcastSignalAsync throw — tests for the dispatcher's
    // error-swallowing path use this to simulate Flowable being unreachable
    // without needing a separate IFlowableClient implementation.
    public Exception? BroadcastSignalThrows { get; set; }

    public Task BroadcastSignalAsync(
        string signalName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"BroadcastSignal:{signalName}");
        BroadcastedSignals.Add((signalName, variables));
        if (BroadcastSignalThrows is not null)
        {
            throw BroadcastSignalThrows;
        }
        return Task.CompletedTask;
    }

    public List<(string ExecutionId, IReadOnlyDictionary<string, object?>? Variables)> SignalledExecutions { get; } = new();

    public Task SignalExecutionAsync(
        string executionId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"SignalExecution:{executionId}");
        SignalledExecutions.Add((executionId, variables));
        return Task.CompletedTask;
    }

    // Tests can seed this to control which execution ids the dispatcher sees
    // when it asks Flowable who is parked on a given intermediate signal
    // catch. Keys are matched ordinally; missing keys yield an empty list.
    public Dictionary<string, IReadOnlyList<string>> WaitingExecutionsBySignal { get; } =
        new(StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
        string signalName,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"ListExecutionsBySignalSubscription:{signalName}");
        return Task.FromResult(WaitingExecutionsBySignal.TryGetValue(signalName, out var ids)
            ? ids
            : (IReadOnlyList<string>)Array.Empty<string>());
    }

    public Task UpdateProcessVariablesAsync(
        string processInstanceId,
        IReadOnlyList<ProcessVariableUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"UpdateVariables:{processInstanceId}");
        if (!VariableUpdatesByInstance.TryGetValue(processInstanceId, out var list))
        {
            list = new List<ProcessVariableUpdate>();
            VariableUpdatesByInstance[processInstanceId] = list;
        }
        list.AddRange(updates);
        return Task.CompletedTask;
    }

    public Dictionary<string, List<ProcessVariableUpdate>> VariableAdditionsByInstance { get; } = new();

    public Task AddProcessVariablesAsync(
        string processInstanceId,
        IReadOnlyList<ProcessVariableUpdate> additions,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"AddVariables:{processInstanceId}");
        if (!VariableAdditionsByInstance.TryGetValue(processInstanceId, out var list))
        {
            list = new List<ProcessVariableUpdate>();
            VariableAdditionsByInstance[processInstanceId] = list;
        }
        list.AddRange(additions);
        return Task.CompletedTask;
    }

    public List<(string ProcessInstanceId, string TargetActivityId)> MoveExecutionStateCalls { get; } = new();

    // Set to make MoveWorkflowExecutionStateAsync throw — tests for the
    // "no active activities" guard case use this to simulate Flowable
    // rejecting an empty cancel list without seeding history rows.
    public Exception? MoveExecutionStateThrows { get; set; }

    public Task MoveWorkflowExecutionStateAsync(
        string processInstanceId,
        string targetActivityId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"MoveExecutionState:{processInstanceId}:{targetActivityId}");
        MoveExecutionStateCalls.Add((processInstanceId, targetActivityId));
        if (MoveExecutionStateThrows is not null)
        {
            throw MoveExecutionStateThrows;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetCompletedAssigneesForActivityAsync(
        string processInstanceId,
        string activityId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CompletedAssignees:{processInstanceId}:{activityId}");
        CompletedAssigneesByActivity.TryGetValue((processInstanceId, activityId), out var list);
        IReadOnlyList<string> result = list?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Dictionary<string, FlowableTaskSummary> TasksById { get; } = new();
    public Dictionary<string, Dictionary<string, JsonElement>> VariablesByProcessInstanceId { get; } = new();

    public Task<FlowableTaskSummary?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetTask:{taskId}");
        TasksById.TryGetValue(taskId, out var task);
        return Task.FromResult<FlowableTaskSummary?>(task);
    }

    public Task<IReadOnlyDictionary<string, JsonElement>> GetProcessInstanceVariablesAsync(
        string processInstanceId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetVariables:{processInstanceId}");
        VariablesByProcessInstanceId.TryGetValue(processInstanceId, out var variables);
        IReadOnlyDictionary<string, JsonElement> result =
            variables ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return Task.FromResult(result);
    }
}
