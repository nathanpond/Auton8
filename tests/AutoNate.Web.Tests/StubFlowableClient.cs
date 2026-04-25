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
        return Task.FromResult<FlowableProcessInstanceSummary?>(null);
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
        return Task.FromResult<IReadOnlyList<FlowableTaskSummary>>(
            Array.Empty<FlowableTaskSummary>());
    }

    public Task CompleteTaskAsync(
        string taskId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"CompleteTask:{taskId}");
        return Task.CompletedTask;
    }
}
