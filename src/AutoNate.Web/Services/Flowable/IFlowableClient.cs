using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Flowable;

public interface IFlowableClient
{
    Task<WorkflowDeploymentInfo> DeployProcessAsync(WorkflowModel model, CancellationToken cancellationToken = default);

    Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default);

    Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(string processDefinitionKey, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);

    Task<FlowableProcessInstanceSummary?> GetProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(CancellationToken cancellationToken = default);

    Task<WorkflowExecutionDiagramDetail> GetWorkflowExecutionDiagramDetailAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task DeleteWorkflowExecutionAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FlowableTaskSummary>> GetTasksByProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUserAsync(string userId, CancellationToken cancellationToken = default);

    // Fan-out helper for "tasks assigned to anyone in this set." Used when a
    // supervisor needs to see tasks for the people they supervise without
    // assuming any back-end query supports list-of-assignees.
    Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    Task CompleteTaskAsync(string taskId, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);

    Task UpdateProcessVariablesAsync(string processInstanceId, IReadOnlyList<ProcessVariableUpdate> updates, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCompletedAssigneesForActivityAsync(string processInstanceId, string activityId, CancellationToken cancellationToken = default);
}
