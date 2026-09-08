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

    /// <summary>
    /// Generated activity id -> the author's element it was expanded from, for
    /// the definition this instance runs (#218).
    /// </summary>
    /// <remarks>
    /// Separate from the diagram detail because the history endpoint needs the
    /// same mapping and has no reason to pull the whole diagram to get it.
    /// </remarks>
    Task<IReadOnlyDictionary<string, string>> GetExpansionSourceMapAsync(
        string processInstanceId, CancellationToken cancellationToken = default);

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

    // Paged enumeration of every runtime task (active + claimed, not yet completed).
    // Used by the projection-framework polling feed to seed the workflow_task_cache
    // without per-user fan-out. `start` is 0-based; `size` caps each page.
    Task<IReadOnlyList<FlowableTaskSummary>> GetRuntimeTasksAsync(int start, int size, CancellationToken cancellationToken = default);

    // Paged enumeration of historic activity instances across every process.
    // Used by the projection-framework history feed to populate the append-only
    // workflow_event_log_cache. `sinceUtc` filters to entries that started after
    // the given time — null means "page from the beginning". Sorted by start
    // time ascending so the consumer can advance a watermark deterministically.
    Task<IReadOnlyList<FlowableHistoricActivityEvent>> GetHistoricActivityEventsAsync(
        int start, int size, DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default);

    // Fan-out helper for "tasks assigned to anyone in this set." Used when a
    // supervisor needs to see tasks for the people they supervise without
    // assuming any back-end query supports list-of-assignees.
    Task<IReadOnlyList<FlowableTaskSummary>> GetTasksAssignedToUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    Task CompleteTaskAsync(string taskId, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);

    // Returns a single runtime task or null if not found / already completed.
    // Used by the task form-config endpoint to look up the task's
    // taskDefinitionKey + processInstanceId + processDefinitionId without
    // pulling a paged list.
    Task<FlowableTaskSummary?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    // Snapshot of every variable on a running process instance, deserialized
    // to JsonElement so the SPA can pass them straight into a form's `data`
    // prop. Returns an empty dictionary if the instance has finished or has
    // no variables yet.
    Task<IReadOnlyDictionary<string, System.Text.Json.JsonElement>> GetProcessInstanceVariablesAsync(
        string processInstanceId,
        CancellationToken cancellationToken = default);

    // Reassigns a runtime user task. Pass null to clear the assignee. Used by
    // the executions admin override surface alongside force-complete.
    Task UpdateTaskAssigneeAsync(string taskId, string? assignee, CancellationToken cancellationToken = default);

    // Sets or clears the due date on a runtime user task. Pass null to clear.
    Task UpdateTaskDueDateAsync(string taskId, DateTimeOffset? dueDate, CancellationToken cancellationToken = default);

    Task UpdateProcessVariablesAsync(string processInstanceId, IReadOnlyList<ProcessVariableUpdate> updates, CancellationToken cancellationToken = default);

    // #158: asks the engine to re-evaluate the instance's conditional events.
    //
    // Flowable does NOT re-evaluate them when a variable changes — established by
    // running it, not by reading docs: a catch on `${approved == true}` stays parked
    // after `approved` is set to true, and advances only when this is called. Same
    // for conditional boundary events, interrupting and not.
    //
    // So this is the link that makes conditional events work at all. Without it they
    // deploy, wait forever, and look exactly like a broken feature — which is the
    // silent no-op this epic exists to end.
    Task EvaluateConditionalEventsAsync(string processInstanceId, CancellationToken cancellationToken = default);

    // Creates one or more new variables on the running instance. Flowable's
    // REST API splits create vs. update — POST .../variables 409s if any
    // entry already exists, and PUT .../variables 4xxs when one doesn't —
    // so the SPA's "Add Variable" UX routes new names through this method
    // and existing names through UpdateProcessVariablesAsync.
    Task AddProcessVariablesAsync(string processInstanceId, IReadOnlyList<ProcessVariableUpdate> additions, CancellationToken cancellationToken = default);

    // Cancels every in-flight activity on the instance and starts execution at
    // `targetActivityId` instead. Implements the admin "Move Execution Here"
    // action — drastic and unguarded: variables persist, but any pending
    // user/service tasks at the cancelled nodes are discarded. Caller is
    // responsible for confirming the move with the operator.
    Task MoveWorkflowExecutionStateAsync(string processInstanceId, string targetActivityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCompletedAssigneesForActivityAsync(string processInstanceId, string activityId, CancellationToken cancellationToken = default);

    // Broadcasts a Flowable signal. Every deployed process whose signal start
    // event references this name spawns a new instance. Variables become
    // process variables on each spawned instance.
    Task BroadcastSignalAsync(string signalName, IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default);

    // Wakes a single waiting execution (intermediate signal catch). Used by the
    // dispatcher's per-execution path that replaces broadcast for non-start signal
    // subscriptions.
    Task SignalExecutionAsync(
        string executionId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);

    // Returns the ids of every runtime execution currently subscribed to the
    // named signal (i.e. paused on an intermediate signal catch). Used by the
    // dispatcher to fan out per-execution signals via SignalExecutionAsync
    // instead of relying on Flowable's broadcast.
    Task<IReadOnlyList<string>> ListExecutionsBySignalSubscriptionAsync(
        string signalName,
        CancellationToken cancellationToken = default);

    // #112. Every execution waiting on `messageName` in a definition, narrowed to
    // those whose `correlationKey` process variable equals `correlationValue`.
    //
    // The narrowing happens in the ENGINE, not here: POST /query/executions takes
    // messageEventSubscriptionName and processInstanceVariables together. That
    // matters for the multi-match rule — the count this returns is the number of
    // instances that genuinely matched, not a page of them, so refusing with "3
    // instances matched" is exact rather than a guess.
    Task<IReadOnlyList<string>> ListExecutionsAwaitingMessageAsync(
        string processDefinitionKey,
        string messageName,
        string? correlationKey,
        string? correlationValue,
        CancellationToken cancellationToken = default);

    // Same, for a receive task. A receive task carries no message subscription, so
    // it is addressed by its activity id — a genuinely different lookup, verified
    // against Flowable 8.0.0.
    Task<IReadOnlyList<string>> ListExecutionsAwaitingReceiveTaskAsync(
        string processDefinitionKey,
        string activityId,
        string? correlationKey,
        string? correlationValue,
        CancellationToken cancellationToken = default);

    // Delivers to one waiting execution. `messageEventReceived` for a message
    // event; the receive-task variant uses `trigger`, because a receive task has
    // no subscription for the engine to match a message name against.
    Task DeliverMessageToExecutionAsync(
        string executionId,
        string messageName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);

    Task TriggerExecutionAsync(
        string executionId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);

    // Starts a new instance through a message start event. Returns the new
    // instance id.
    // #113. The instances a call activity in this one started.
    //
    // The engine records the relationship (superProcessInstanceId) and nothing in
    // the app surfaced it. That matters more than it sounds: while a parent waits
    // on a call activity its OWN task list is empty, so from the parent alone a
    // running child is indistinguishable from a hung process.
    Task<IReadOnlyList<FlowableProcessInstanceSummary>> GetChildProcessInstancesAsync(
        string parentProcessInstanceId,
        CancellationToken cancellationToken = default);

    Task<string> StartProcessInstanceByMessageAsync(
        string messageName,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);
}
