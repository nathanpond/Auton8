namespace AutoNate.Web.Models;

public sealed record class FlowableProcessDefinitionSummary
{
    public string Id { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Version { get; init; }

    public string DeploymentId { get; init; } = string.Empty;

    // Flowable's suspension flag on the process definition. When true, no new
    // instances can be started for this definition; existing runs continue.
    public bool Suspended { get; init; }
}

public sealed record class FlowableProcessInstanceSummary
{
    public string Id { get; init; } = string.Empty;

    // Display name set when the run was started (or null).
    public string? Name { get; init; }

    public string ProcessDefinitionId { get; init; } = string.Empty;

    public string? ActivityId { get; init; }

    public bool Suspended { get; init; }

    // Flowable's `startUserId` — the user who started the process instance.
    // Drives the `startedby` selector tag.
    public string? StartUserId { get; init; }
}

public sealed record class FlowableHistoricProcessInstanceSummary
{
    public string Id { get; init; } = string.Empty;

    public string ProcessDefinitionId { get; init; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }
}

public sealed record class FlowableTaskSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? TaskDefinitionKey { get; init; }

    public string? Assignee { get; init; }

    public string? ProcessInstanceId { get; init; }

    // Per-instance display name of the task's parent process. Mirrors
    // WorkflowExecutionSummary.Name. Null when the run wasn't named.
    public string? ProcessInstanceName { get; init; }

    public string? ProcessDefinitionId { get; init; }

    public string? ProcessDefinitionName { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    public DateTimeOffset? DueDate { get; init; }
}

public sealed record class WorkflowExecutionSummary
{
    public string Id { get; set; } = string.Empty;

    // Flowable's per-instance display name (`act_hi_procinst.name_`). Set at
    // start time. Null when the run wasn't given a name.
    public string? Name { get; set; }

    public string? WorkflowModelName { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? LastActivityAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? CurrentStep { get; set; }

    // Carried so list-endpoint visibility filters can evaluate predicate
    // selectors (`processkey`, `definitionkey`, `startedby`) without a
    // per-row Flowable round-trip.
    public string? ProcessDefinitionId { get; set; }

    public string? StartUserId { get; set; }
}

public sealed record class WorkflowExecutionDiagramDetail
{
    public string ExecutionId { get; init; } = string.Empty;

    // Display name from Flowable (mirrors WorkflowExecutionSummary.Name).
    public string? Name { get; init; }

    public string BpmnXml { get; init; } = string.Empty;

    public IReadOnlyList<string> CompletedActivityIds { get; init; } = [];

    public IReadOnlyList<string> CurrentActivityIds { get; init; } = [];

    // Activities that were in flight when the process was cancelled. Always
    // empty for non-cancelled executions. Rendered with their own highlight
    // so the diagram doesn't pretend they finished normally.
    public IReadOnlyList<string> CancelledActivityIds { get; init; } = [];

    // Activities that produced a job.execution.failed event for this process
    // instance. Populated from the workflow_execution_errors table.
    public IReadOnlyList<string> FailedActivityIds { get; init; } = [];

    // Latest non-empty error message per failed activity. Sourced from the
    // workflow_execution_errors table. Only populated for activity ids that
    // also appear in FailedActivityIds AND have at least one captured message
    // (rows from before the capture feature shipped won't surface here).
    public IReadOnlyDictionary<string, string> ErrorMessagesByActivityId { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<FlowableProcessVariable> Variables { get; init; } = [];
}

public sealed record class FlowableProcessVariable
{
    public string Name { get; init; } = string.Empty;

    public string? Type { get; init; }

    public string? Value { get; init; }
}

// One row in an execution's chronological history. Sourced from Flowable's
// historic-activity-instances endpoint sorted ascending by start time. The
// SPA renders this list in the History tab on the workflow execution modal.
public sealed record class WorkflowExecutionHistoryEvent
{
    public string ActivityId { get; init; } = string.Empty;

    public string? ActivityName { get; init; }

    // Flowable's BPMN element type — userTask, serviceTask, startEvent,
    // endEvent, exclusiveGateway, etc.
    public string? ActivityType { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public long? DurationMs { get; init; }

    // Populated for userTask rows.
    public string? Assignee { get; init; }

    public string? TaskId { get; init; }

    // Set by Flowable when the row was halted by a process-level cancel
    // (or other delete) rather than completing through normal flow.
    public string? DeleteReason { get; init; }

    // Populated only on userTask rows where AutoNate has a record of who
    // triggered the completion (workflow_task_completions). Distinct from
    // Assignee when an admin force-completed someone else's task.
    public string? CompletedByUserId { get; init; }

    // True when CompletedByUserId came from the override endpoint.
    public bool? IsOverride { get; init; }

    // True when at least one workflow_execution_errors row exists for this
    // activityId in this process — i.e. the node failed at least once.
    public bool? IsErrored { get; init; }

    // Latest captured error message from workflow_execution_errors for this
    // activityId. Often null today (the Flowable extension doesn't yet
    // capture exception messages — see followup task).
    public string? ErrorMessage { get; init; }

    // Latest captured full stack trace from workflow_execution_errors for this
    // activityId. Often null on legacy rows; the SPA hides the "Show stack
    // trace" toggle when this is null.
    public string? ErrorStackTrace { get; init; }

    // Number of recorded failures for this activity in this process.
    // Useful when an activity errored, retried, then succeeded — the row
    // looks "completed" but the retry count tells the real story.
    public int? ErrorCount { get; init; }
}

// One row in the Execution Log tab. Either a variable change or a task
// lifecycle event (created / claimed / completed / cancelled). The Kind
// discriminator picks which nested record is populated.
public sealed record class WorkflowExecutionLogEntry
{
    // Discriminator: "variable-update", "task-created", "task-claimed",
    // "task-completed", "task-cancelled", "error".
    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset? OccurredAtUtc { get; init; }

    // Populated when Kind == "variable-update".
    public WorkflowExecutionLogVariableUpdate? VariableUpdate { get; init; }

    // Populated when Kind starts with "task-".
    public WorkflowExecutionLogTask? Task { get; init; }

    // Populated when Kind == "error".
    public WorkflowExecutionLogError? Error { get; init; }
}

public sealed record class WorkflowExecutionLogVariableUpdate
{
    public string Name { get; init; } = string.Empty;

    public string? Type { get; init; }

    // Flattened display value (FormatVariableValue handles json/number/bool).
    public string? Value { get; init; }

    public int? Revision { get; init; }

    // Task that drove the update, when the change happened inside one.
    public string? TaskId { get; init; }

    public string? ActivityInstanceId { get; init; }
}

public sealed record class WorkflowExecutionLogTask
{
    public string TaskId { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? TaskDefinitionKey { get; init; }

    public string? Assignee { get; init; }

    public string? Owner { get; init; }

    public string? FormKey { get; init; }

    public int? Priority { get; init; }

    public DateTimeOffset? DueAtUtc { get; init; }

    // Set on the task-cancelled entry from the historic task's deleteReason.
    public string? DeleteReason { get; init; }

    // Populated only on task-completed entries when AutoNate has a record of
    // who triggered the completion (workflow_task_completions). Distinct
    // from Assignee, which is what Flowable stored — they differ when an
    // admin force-completes someone else's task.
    public string? CompletedByUserId { get; init; }

    // True when CompletedByUserId came from the override endpoint.
    public bool? IsOverride { get; init; }
}

// One log entry per recorded JOB_EXECUTION_FAILURE row. Each retry of a
// failing service task produces a separate row in workflow_execution_errors,
// so the log shows the full retry timeline.
public sealed record class WorkflowExecutionLogError
{
    public string ActivityId { get; init; } = string.Empty;

    // Resolved from Flowable's historic-activity-instances at read time —
    // the Flowable event payload doesn't reliably carry the activity name,
    // so we look it up alongside the diagram/history fetch.
    public string? ActivityName { get; init; }

    public string? ErrorMessage { get; init; }

    // Flowable engine event type, e.g. "JOB_EXECUTION_FAILURE". Useful for
    // distinguishing job failures from other future error sources.
    public string? RawFlowableEventType { get; init; }
}

// Override-write payload for a single process variable. The diagram-detail GET
// flattens Flowable's typed values (json/number/bool) to a string via
// FormatVariableValue; the SPA parses the string back into a runtime value
// using the variable's Type as a hint before sending it here. When Type is
// null Flowable infers from the value's JSON kind. A null Value clears the
// variable on Flowable's side.
public sealed record class ProcessVariableUpdate
{
    public required string Name { get; init; }

    public object? Value { get; init; }

    public string? Type { get; init; }
}
