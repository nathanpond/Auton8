using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Records;

namespace AutoNate.Web.Services.Events;

// Hardcoded catalog of every event AutoNate's services publish to the bus.
// Two publishers today:
//   - The Flowable extension publishes workflow telemetry to
//     `workflow.execution.events`.
//   - AutoNate.Web itself publishes record lifecycle events to
//     `record.events` (see Services/Records/RecordEventPublisher.cs).
// Adding new publishers means appending entries here; the SPA Events page and
// the signal start event modal both consume the catalog.
public static class EventCatalog
{
    private static readonly EventCatalogPayloadField[] EnvelopeFields =
    [
        new("eventId", "string (UUID)", "Unique identifier for this event occurrence."),
        new("eventType", "string", "Friendly event type — one of the values listed below (e.g. 'task.created' or 'record.created')."),
        new("occurredAtUtc", "string (ISO 8601)", "UTC timestamp at which the publisher emitted the event after committing the underlying state change."),
        new("sourceAppId", "string", "Identifier of the publishing application (e.g. 'autonate.web' for record events, configured per Flowable extension for workflow events).")
    ];

    private static readonly EventCatalogPayloadField[] WorkflowPayloadFields =
    [
        new("processInstanceId", "string", "Flowable process instance ID. Identifies the running workflow."),
        new("processDefinitionId", "string", "Versioned Flowable process definition ID (e.g. 'my-process:3:abc...')."),
        new("processDefinitionKey", "string", "Stable BPMN process key — the same across versions of the same model."),
        new("processDefinitionName", "string", "Human-readable process definition name."),
        new("activityId", "string | null", "BPMN element ID for the activity in scope (null for some process-level events)."),
        new("activityName", "string | null", "Display name of the activity in scope."),
        new("taskId", "string | null", "User task ID — populated only on task.* events."),
        new("taskName", "string | null", "User task name — populated only on task.* events."),
        new("assignee", "string | null", "User the task is currently assigned to — populated only on task.* events."),
        new("tenantId", "string | null", "Flowable tenant identifier when multi-tenancy is enabled."),
        new("rawFlowableEventType", "string", "Original Flowable engine event type (e.g. 'TASK_CREATED'). Useful for debugging.")
    ];

    private static readonly EventCatalogPayloadField[] RecordPayloadFields =
    [
        new("recordId", "string (UUID)", "Stable identifier of the record."),
        new("key", "string", "Human-readable record key (e.g. 'ACC-42')."),
        new("recordTypeId", "string (UUID)", "Identifier of the record's type."),
        new("name", "string", "Display name of the record at the time of the event."),
        new("status", "string | null", "Current status value (null when no status is set)."),
        new("previousStatus", "string | null", "Status value before the change. Populated on `record.status.changed`; null on other event types."),
        new("changedFields", "string[]", "Names of top-level fields that changed (e.g. 'name', 'assigneeIds', 'status', 'dueDate', 'values.<fieldKey>'). Empty for `record.created`; ['isArchived'] for `record.deleted`."),
        new("assigneeIds", "string[] (UUIDs)", "Current assignees of the record."),
        new("isArchived", "boolean", "Whether the record is archived. `record.deleted` always carries true."),
        new("actorId", "string (UUID)", "User who performed the action that produced this event.")
    ];

    public static readonly EventCatalogPayloadField[] PayloadFields = EnvelopeFields;

    public static readonly EventCatalogTransport[] Transports =
    [
        new(
            BusWatcherStreamService.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "The autonate-flowable-events extension running inside Flowable."),
        new(
            DaprRecordEventPublisher.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the record store after each create / update / archive commit.")
    ];

    public static readonly EventCatalogCategory[] Categories =
    [
        new(
            "Process",
            "Lifecycle events for a workflow process instance — emitted once Flowable commits the underlying transaction.",
            WorkflowPayloadFields,
            [
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "process.started",
                    "A new process instance has begun execution.",
                    "Flowable raises PROCESS_STARTED, typically after a successful POST to start a workflow or a timer/message start event.",
                    [
                        "processInstanceId, processDefinitionId, processDefinitionKey, processDefinitionName populated.",
                        "activityId reflects the BPMN start event element (no taskId)."
                    ]),
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "process.completed",
                    "A process instance reached a normal end event and finished successfully.",
                    "Flowable raises PROCESS_COMPLETED on a successful end event.",
                    ["No taskId. activityId is the end event element."]),
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "process.completed.error",
                    "A process instance ended via an error end event.",
                    "Flowable raises PROCESS_COMPLETED_WITH_ERROR_END_EVENT — the workflow terminated through an error boundary path.",
                    []),
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "process.cancelled",
                    "A process instance was cancelled before it could complete.",
                    "Flowable raises PROCESS_CANCELLED — e.g. an admin/API delete, terminate end event, or cancellation boundary.",
                    [])
            ]),
        new(
            "Activity",
            "Step-level events emitted as control flows through individual BPMN elements (service tasks, gateways, sub-processes, etc.).",
            WorkflowPayloadFields,
            [
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "activity.started",
                    "An activity (BPMN element) began execution.",
                    "Flowable raises ACTIVITY_STARTED for the element.",
                    ["activityId and activityName identify the BPMN element."]),
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "activity.completed",
                    "An activity finished.",
                    "Flowable raises ACTIVITY_COMPLETED for the element.",
                    [])
            ]),
        new(
            "User Task",
            "Events for human-assigned tasks. These are the events most often used to drive inbox/UI updates.",
            WorkflowPayloadFields,
            [
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "task.created",
                    "A new user task is now waiting on a human.",
                    "Flowable raises TASK_CREATED on entry to a user task element.",
                    [
                        "taskId, taskName populated.",
                        "assignee populated if the task is created with an assignee already set."
                    ]),
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "task.assigned",
                    "A task's assignee has changed (or been set for the first time).",
                    "Flowable raises TASK_ASSIGNED whenever the task's assignee field is set or updated.",
                    ["assignee reflects the new owner."]),
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "task.completed",
                    "A user task was completed and the workflow can move forward.",
                    "Flowable raises TASK_COMPLETED.",
                    [
                        "Includes the assignee that completed the task in 'assignee'.",
                        "Note: also fired by the 'force complete' admin action."
                    ])
            ]),
        new(
            "Job",
            "Events surfaced by the Flowable async job executor — useful for surfacing background failures.",
            WorkflowPayloadFields,
            [
                new EventCatalogEntry(
                    BusWatcherStreamService.TopicName,
                    "job.execution.failed",
                    "A Flowable async job (timer, async continuation, etc.) threw an exception.",
                    "Flowable raises JOB_EXECUTION_FAILURE. Typically retried by the engine; surfaced here so operators can see failures in real time.",
                    [])
            ]),
        new(
            "Record",
            "Lifecycle events for AutoNate records — emitted by the record store after each create / update / archive transaction commits.",
            RecordPayloadFields,
            [
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Created,
                    "A new record has been created.",
                    "Fires from EfCoreRecordStore.CreateAsync once the create transaction (record + history + edges) commits.",
                    [
                        "changedFields is empty (the record is new — every field is its initial value).",
                        "previousStatus is always null."
                    ]),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Updated,
                    "A record's name, assignees, status, due date, or field values changed (or it was unarchived).",
                    "Fires from EfCoreRecordStore.UpdateAsync after commit when at least one field changed, and from SetArchivedAsync when archived flips from true to false.",
                    [
                        "changedFields lists every field touched (e.g. 'name', 'status', 'values.priority').",
                        "When status changed, a separate `record.status.changed` event is also published."
                    ]),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Deleted,
                    "A record was archived (soft-deleted).",
                    "Fires from EfCoreRecordStore.SetArchivedAsync(archived: true) — the record row stays in the database but is hidden from default reads.",
                    [
                        "isArchived is always true.",
                        "changedFields is ['isArchived']."
                    ]),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.StatusChanged,
                    "A record's status field changed value.",
                    "Fires from EfCoreRecordStore.UpdateAsync after commit, in addition to `record.updated`, whenever the status differs from its previous value.",
                    [
                        "previousStatus carries the value before the change (may be null).",
                        "status carries the new value (may be null when status was cleared)."
                    ])
            ])
    ];

    // Flat (topic, eventType) projection for autocomplete consumers.
    public static IEnumerable<EventCatalogEntry> AllEntries =>
        Categories.SelectMany(category => category.Events);
}

public sealed record EventCatalogTransport(string Topic, string Description, string Source);

public sealed record EventCatalogPayloadField(string Name, string Type, string Description);

public sealed record EventCatalogCategory(
    string Title,
    string Description,
    IReadOnlyList<EventCatalogPayloadField> PayloadFields,
    IReadOnlyList<EventCatalogEntry> Events);

public sealed record EventCatalogEntry(
    string Topic,
    string EventType,
    string Summary,
    string FiresWhen,
    IReadOnlyList<string> PayloadHighlights);
