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

    public Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(
        string processDefinitionKey,
        string? name = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Start:{processDefinitionKey}:{name ?? "(unnamed)"}");
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

    public Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(
        CancellationToken cancellationToken = default)
    {
        Calls.Add("ListExecutions");
        return Task.FromResult<IReadOnlyList<WorkflowExecutionSummary>>(
            Array.Empty<WorkflowExecutionSummary>());
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

    public Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"TasksByInstance:{processInstanceId}");
        return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(
            Array.Empty<FlowableTaskSummary>());
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
}
