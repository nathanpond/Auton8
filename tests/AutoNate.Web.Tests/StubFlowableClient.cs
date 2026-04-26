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

    public Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(
        string processDefinitionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add($"GetLatest:{processDefinitionKey}");
        return Task.FromResult<FlowableProcessDefinitionSummary?>(null);
    }

    public Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(
        string processDefinitionKey,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Start:{processDefinitionKey}");
        return Task.FromResult(new FlowableProcessInstanceSummary());
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

    public Task DeleteWorkflowExecutionAsync(
        string processInstanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"DeleteExecution:{processInstanceId}");
        return Task.CompletedTask;
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

    public Dictionary<string, List<ProcessVariableUpdate>> VariableUpdatesByInstance { get; } = new();

    public Dictionary<(string ProcessInstanceId, string ActivityId), List<string>> CompletedAssigneesByActivity { get; } = new();

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
