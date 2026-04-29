using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Flowable;

public interface IFlowableClient
{
    Task<WorkflowDeploymentInfo> DeployProcessAsync(WorkflowModel model, CancellationToken cancellationToken = default);

    Task<FlowableProcessDefinitionSummary?> GetLatestProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default);

    // Bulk fetch of every "latest=true" process definition. Used by the
    // workflow list endpoint to populate the per-workflow IsSuspended flag in
    // a single Flowable round-trip rather than one call per workflow.
    Task<IReadOnlyList<FlowableProcessDefinitionSummary>> GetLatestProcessDefinitionsAsync(CancellationToken cancellationToken = default);

    // Suspends the latest process definition for this key. Existing running
    // instances keep going; new starts are rejected by Flowable until
    // ActivateProcessDefinitionAsync is called.
    Task SuspendProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default);

    Task ActivateProcessDefinitionAsync(string processDefinitionKey, CancellationToken cancellationToken = default);

    Task<FlowableProcessInstanceSummary> StartProcessInstanceAsync(string processDefinitionKey, string? name = null, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);

    // Count of all process instances (running + finished) for a definition
    // key. Drives "ModelName (N+1)" auto-naming on workflow start.
    Task<int> GetHistoricProcessInstanceCountByDefinitionKeyAsync(string processDefinitionKey, CancellationToken cancellationToken = default);

    Task<FlowableProcessInstanceSummary?> GetProcessInstanceAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowExecutionSummary>> GetWorkflowExecutionsAsync(CancellationToken cancellationToken = default);

    Task<WorkflowExecutionDiagramDetail> GetWorkflowExecutionDiagramDetailAsync(string processInstanceId, CancellationToken cancellationToken = default);

    // Chronological per-activity history for a process instance, ascending by
    // start time. Drives the History tab on the workflow execution modal.
    Task<IReadOnlyList<WorkflowExecutionHistoryEvent>> GetWorkflowExecutionHistoryAsync(string processInstanceId, CancellationToken cancellationToken = default);

    // Variable updates + task lifecycle events (created/claimed/completed/
    // cancelled) merged and sorted ascending by occurrence. Drives the
    // Execution Log tab on the workflow execution modal.
    Task<IReadOnlyList<WorkflowExecutionLogEntry>> GetWorkflowExecutionLogAsync(string processInstanceId, CancellationToken cancellationToken = default);

    Task DeleteWorkflowExecutionAsync(string processInstanceId, CancellationToken cancellationToken = default);

    // Wipes every process instance in Flowable — runtime + history. Used by
    // the executions admin page to clear noise during signal-event debugging.
    // Returns the number of instances deleted so the caller can surface it.
    Task<int> DeleteAllWorkflowExecutionsAsync(CancellationToken cancellationToken = default);

    // Stops a running process instance and leaves the historic record in
    // place so the executions list can show it as "Cancelled". No-op if the
    // instance has already finished.
    Task CancelWorkflowExecutionAsync(string processInstanceId, CancellationToken cancellationToken = default);

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

    // Broadcasts a Flowable signal. Every deployed process whose signal start
    // event references this name spawns a new instance. Variables become
    // process variables on each spawned instance.
    Task BroadcastSignalAsync(string signalName, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);
}
