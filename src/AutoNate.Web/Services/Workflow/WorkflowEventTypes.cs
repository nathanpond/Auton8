namespace AutoNate.Web.Services.Workflow;

// Topic + event-type names for the workflow.events bus topic. Phase 3 of the
// audit-events plan introduces this domain. Distinct from the Flowable-side
// workflow.execution.events (system-generated process telemetry); this topic
// carries user-initiated commands — saving/publishing/pausing/resuming
// workflow models and the admin execution actions (variable edits, force
// task completion, reassignment, due-date changes, state moves, cancel,
// delete).
public static class WorkflowAdminEventTopic
{
    // Hyphenated root so subjects are disjoint from Flowable's
    // workflow.execution.> stream — JetStream rejects overlapping subjects.
    public const string TopicRoot = "workflow-admin";
    public const string TopicName = "workflow-admin.events";
}

public static class WorkflowResourceKinds
{
    public const string WorkflowModel = "workflow.model";
    public const string Execution = "workflow.execution";
    public const string Task = "workflow.task";
}

public static class WorkflowAdminEventTypes
{
    // Workflow model lifecycle
    public const string ModelSaved = "workflow.model.saved";
    public const string ModelPublished = "workflow.model.published";
    public const string ModelPaused = "workflow.model.paused";
    public const string ModelResumed = "workflow.model.resumed";
    public const string ModelStarted = "workflow.start.invoked";

    // Execution admin commands (user-initiated; system-generated equivalents
    // live on workflow.execution.events from the Flowable extension).
    public const string ExecutionVariablesSet = "workflow.execution.variables.set";
    public const string ExecutionVariablesAdded = "workflow.execution.variables.added";
    public const string ExecutionStateMoved = "workflow.execution.state.moved";
    public const string ExecutionCancelled = "workflow.execution.cancelled";
    public const string ExecutionDeleted = "workflow.execution.deleted";
    public const string ExecutionsBulkDeleted = "workflow.execution.deleted.all";

    // Task admin commands
    public const string TaskForceCompleted = "workflow.task.force.completed";
    public const string TaskReassigned = "workflow.task.reassigned";
    public const string TaskDueDateChanged = "workflow.task.due.date.changed";
    public const string TaskCompleted = "workflow.task.completed";

    // View events (Phase 4)
    public const string ModelListViewed = "workflow.model.list.viewed";
    public const string ModelViewed = "workflow.model.viewed";
    public const string ModelLatestViewed = "workflow.model.latest.viewed";
    public const string ModelVersionsViewed = "workflow.model.versions.viewed";
    public const string ExecutionListViewed = "workflow.execution.list.viewed";
    public const string ExecutionDiagramViewed = "workflow.execution.diagram.viewed";
    public const string ExecutionHistoryViewed = "workflow.execution.history.viewed";
    public const string ExecutionLogViewed = "workflow.execution.log.viewed";
    public const string ExecutionTasksViewed = "workflow.execution.tasks.viewed";
    public const string ExecutionCompletedAssigneesViewed = "workflow.execution.completed-assignees.viewed";
    public const string TasksAssignedToMeViewed = "workflow.task.assigned-to-me.viewed";
    public const string TasksAssignedToTeamViewed = "workflow.task.assigned-to-team.viewed";
}
