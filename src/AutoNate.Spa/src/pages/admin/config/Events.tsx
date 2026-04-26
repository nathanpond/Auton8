type EventCategory = {
  title: string;
  description: string;
  events: EventEntry[];
};

type EventEntry = {
  eventType: string;
  summary: string;
  firesWhen: string;
  payloadHighlights?: string[];
};

const TOPIC_NAME = "workflow.execution.events";

const PAYLOAD_FIELDS: { name: string; type: string; description: string }[] = [
  { name: "eventId", type: "string (UUID)", description: "Unique identifier for this event occurrence." },
  { name: "eventType", type: "string", description: "Friendly event type — one of the values listed below (e.g. 'task.created')." },
  { name: "occurredAtUtc", type: "string (ISO 8601)", description: "UTC timestamp at which the engine emitted the event after transaction commit." },
  { name: "processInstanceId", type: "string", description: "Flowable process instance ID. Identifies the running workflow." },
  { name: "processDefinitionId", type: "string", description: "Versioned Flowable process definition ID (e.g. 'my-process:3:abc...')." },
  { name: "processDefinitionKey", type: "string", description: "Stable BPMN process key — the same across versions of the same model." },
  { name: "processDefinitionName", type: "string", description: "Human-readable process definition name." },
  { name: "activityId", type: "string | null", description: "BPMN element ID for the activity in scope (null for some process-level events)." },
  { name: "activityName", type: "string | null", description: "Display name of the activity in scope." },
  { name: "taskId", type: "string | null", description: "User task ID — populated only on task.* events." },
  { name: "taskName", type: "string | null", description: "User task name — populated only on task.* events." },
  { name: "assignee", type: "string | null", description: "User the task is currently assigned to — populated only on task.* events." },
  { name: "tenantId", type: "string | null", description: "Flowable tenant identifier when multi-tenancy is enabled." },
  { name: "rawFlowableEventType", type: "string", description: "Original Flowable engine event type (e.g. 'TASK_CREATED'). Useful for debugging." },
  { name: "sourceAppId", type: "string", description: "Identifier of the publishing application (configured in the Flowable extension)." }
];

const CATEGORIES: EventCategory[] = [
  {
    title: "Process",
    description:
      "Lifecycle events for a workflow process instance — emitted once Flowable commits the underlying transaction.",
    events: [
      {
        eventType: "process.started",
        summary: "A new process instance has begun execution.",
        firesWhen:
          "Flowable raises PROCESS_STARTED, typically after a successful POST to start a workflow or a timer/message start event.",
        payloadHighlights: [
          "processInstanceId, processDefinitionId, processDefinitionKey, processDefinitionName populated.",
          "activityId reflects the BPMN start event element (no taskId)."
        ]
      },
      {
        eventType: "process.completed",
        summary: "A process instance reached a normal end event and finished successfully.",
        firesWhen: "Flowable raises PROCESS_COMPLETED on a successful end event.",
        payloadHighlights: ["No taskId. activityId is the end event element."]
      },
      {
        eventType: "process.completed.error",
        summary: "A process instance ended via an error end event.",
        firesWhen:
          "Flowable raises PROCESS_COMPLETED_WITH_ERROR_END_EVENT — the workflow terminated through an error boundary path."
      },
      {
        eventType: "process.cancelled",
        summary: "A process instance was cancelled before it could complete.",
        firesWhen:
          "Flowable raises PROCESS_CANCELLED — e.g. an admin/API delete, terminate end event, or cancellation boundary."
      }
    ]
  },
  {
    title: "Activity",
    description:
      "Step-level events emitted as control flows through individual BPMN elements (service tasks, gateways, sub-processes, etc.).",
    events: [
      {
        eventType: "activity.started",
        summary: "An activity (BPMN element) began execution.",
        firesWhen: "Flowable raises ACTIVITY_STARTED for the element.",
        payloadHighlights: ["activityId and activityName identify the BPMN element."]
      },
      {
        eventType: "activity.completed",
        summary: "An activity finished.",
        firesWhen: "Flowable raises ACTIVITY_COMPLETED for the element."
      }
    ]
  },
  {
    title: "User Task",
    description:
      "Events for human-assigned tasks. These are the events most often used to drive inbox/UI updates.",
    events: [
      {
        eventType: "task.created",
        summary: "A new user task is now waiting on a human.",
        firesWhen: "Flowable raises TASK_CREATED on entry to a user task element.",
        payloadHighlights: [
          "taskId, taskName populated.",
          "assignee populated if the task is created with an assignee already set."
        ]
      },
      {
        eventType: "task.assigned",
        summary: "A task's assignee has changed (or been set for the first time).",
        firesWhen:
          "Flowable raises TASK_ASSIGNED whenever the task's assignee field is set or updated.",
        payloadHighlights: ["assignee reflects the new owner."]
      },
      {
        eventType: "task.completed",
        summary: "A user task was completed and the workflow can move forward.",
        firesWhen: "Flowable raises TASK_COMPLETED.",
        payloadHighlights: [
          "Includes the assignee that completed the task in 'assignee'.",
          "Note: also fired by the 'force complete' admin action."
        ]
      }
    ]
  },
  {
    title: "Job",
    description:
      "Events surfaced by the Flowable async job executor — useful for surfacing background failures.",
    events: [
      {
        eventType: "job.execution.failed",
        summary: "A Flowable async job (timer, async continuation, etc.) threw an exception.",
        firesWhen:
          "Flowable raises JOB_EXECUTION_FAILURE. Typically retried by the engine; surfaced here so operators can see failures in real time."
      }
    ]
  }
];

export default function Events() {
  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Events</h1>
        <p className="page-head-copy">
          Reference for every event the application publishes to its message bus. All events
          below are produced by the Flowable workflow engine extension and delivered through
          Dapr pub/sub on a single topic; a single live feed of these events is available on
          the <strong>Bus Watcher</strong> page.
        </p>
      </div>

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading">
          <h4 className="panel-title">Transport</h4>
        </div>
        <div className="panel-body">
          <dl className="row mb-0">
            <dt className="col-sm-3">Broker</dt>
            <dd className="col-sm-9">Dapr pub/sub (NATS JetStream in the default deployment).</dd>

            <dt className="col-sm-3">Topic</dt>
            <dd className="col-sm-9">
              <code>{TOPIC_NAME}</code>
              <div className="text-muted small mt-1">
                Every event below is published to this single topic. The specific kind of
                event is carried in the <code>eventType</code> field on the payload.
              </div>
            </dd>

            <dt className="col-sm-3">Encoding</dt>
            <dd className="col-sm-9">
              CloudEvents envelope, JSON payload. Cross-cutting CloudEvents headers
              (<code>ce-*</code>) accompany each delivery.
            </dd>

            <dt className="col-sm-3">Source</dt>
            <dd className="col-sm-9">
              The <code>autonate-flowable-events</code> extension running inside Flowable.
              The application itself does not publish workflow events directly.
            </dd>
          </dl>
        </div>
      </div>

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading">
          <h4 className="panel-title">Common payload</h4>
        </div>
        <div className="panel-body">
          <p className="text-muted mb-2">
            Every event shares the same payload shape. Fields not relevant to a particular
            event type are omitted (sent as <code>null</code>).
          </p>
          <div className="table-responsive">
            <table className="table table-sm table-striped mb-0">
              <thead>
                <tr>
                  <th style={{ width: "20%" }}>Field</th>
                  <th style={{ width: "20%" }}>Type</th>
                  <th>Description</th>
                </tr>
              </thead>
              <tbody>
                {PAYLOAD_FIELDS.map((field) => (
                  <tr key={field.name}>
                    <td>
                      <code>{field.name}</code>
                    </td>
                    <td className="text-muted">{field.type}</td>
                    <td>{field.description}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {CATEGORIES.map((category) => (
        <div key={category.title} className="panel panel-inverse mb-3">
          <div className="panel-heading">
            <h4 className="panel-title">{category.title}</h4>
          </div>
          <div className="panel-body">
            <p className="text-muted">{category.description}</p>
            <div className="table-responsive">
              <table className="table table-sm mb-0">
                <thead>
                  <tr>
                    <th style={{ width: "22%" }}>Event</th>
                    <th>What it means / when it fires</th>
                  </tr>
                </thead>
                <tbody>
                  {category.events.map((evt) => (
                    <tr key={evt.eventType}>
                      <td>
                        <code>{evt.eventType}</code>
                      </td>
                      <td>
                        <div>
                          <strong>{evt.summary}</strong>
                        </div>
                        <div className="text-muted small mt-1">{evt.firesWhen}</div>
                        {evt.payloadHighlights && evt.payloadHighlights.length > 0 && (
                          <ul className="small mt-2 mb-0">
                            {evt.payloadHighlights.map((line, idx) => (
                              <li key={idx}>{line}</li>
                            ))}
                          </ul>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      ))}
    </>
  );
}
