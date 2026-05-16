using AutoNate.Web.Services.Agent;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Services.Events;

// Hardcoded catalog of every event AutoNate's services publish to the bus.
// Two publishers today:
//   - The Flowable extension publishes workflow telemetry to
//     `workflow.execution.events`.
//   - AutoNate.Web itself publishes record lifecycle events to
//     `record.events` (see Services/Records/RecordEventPublisher.cs).
// Adding new publishers means appending entries here; the SPA Events page and
// the signal start event modal both consume the catalog.
//
// **Delivery durability** (Phase 5 of the audit-events plan): every event
// published from AutoNate.Web is enqueued in the audit_outbox table before it
// hits the bus. AuditOutboxDispatcher polls the table, posts to Dapr, and
// retries with exponential backoff on failure (see Services/Events/
// AuditOutboxDispatcher.cs). This means events survive Dapr/NATS hiccups —
// they sit in the outbox until the dispatcher catches up. The enqueue itself
// is NOT yet atomic with the upstream domain transaction (a row written from
// a fresh DbContext after the domain commit), so a crash between domain
// commit and outbox write can still drop an event. Closing that gap is a
// follow-up refactor.
public static class EventCatalog
{
    private static readonly EventCatalogPayloadField[] EnvelopeFields =
    [
        new("eventId", "string (UUID)", "Unique identifier for this event occurrence."),
        new("eventType", "string", "Friendly event type — one of the values listed below (e.g. 'task.created' or 'record.created')."),
        new("occurredAtUtc", "string (ISO 8601)", "UTC timestamp at which the publisher emitted the event after committing the underlying state change. (DEPRECATED at the top level; prefer auditContext.occurredAtUtc — kept for back-compat with existing consumers.)"),
        new("sourceAppId", "string", "Identifier of the publishing application (e.g. 'autonate.web' for record events, configured per Flowable extension for workflow events). (DEPRECATED at the top level; prefer auditContext.sourceAppId.)"),
        new("auditContext", "object", "Shared audit context attached to every event published from autonate.web. Carries actorId, actorUserName, occurredAtUtc, requestId, correlationId, ipAddress, userAgent, sourceAppId, httpMethod, routePath, authOutcome, authDecisionReason. Populated automatically by the publisher from the active HTTP request; null on Flowable-side events that originate outside an HTTP request."),
        new("auditContext.actorId", "string (UUID) | null", "User who initiated the action. Null only for Flowable-system events and pre-authentication flows (e.g. failed login)."),
        new("auditContext.actorUserName", "string | null", "Denormalized username of the actor at the time of the event."),
        new("auditContext.occurredAtUtc", "string (ISO 8601)", "UTC timestamp at which the publisher emitted the event."),
        new("auditContext.requestId", "string", "HttpContext trace identifier — links the event to request-log lines."),
        new("auditContext.correlationId", "string | null", "Value of the X-Correlation-Id (or X-Request-Id) header if the caller supplied one."),
        new("auditContext.ipAddress", "string", "Originating client IP after honoring X-Forwarded-For. Empty for non-HTTP-originated events."),
        new("auditContext.userAgent", "string", "User-Agent header, truncated to 512 chars."),
        new("auditContext.sourceAppId", "string", "Publishing application identifier, e.g. 'autonate.web'."),
        new("auditContext.httpMethod", "string", "HTTP method of the request that produced the event."),
        new("auditContext.routePath", "string", "Route template (PII-safe — no path values), e.g. '/api/records/{id}'."),
        new("auditContext.authOutcome", "string", "One of 'Allowed', 'Denied', 'Anonymous'. Always 'Allowed' for successful mutations and view events; populated from the authorization deny path for access-denied audit events."),
        new("auditContext.authDecisionReason", "string | null", "Populated when authOutcome is 'Denied' with the human-readable reason from AuthDecision.Reason.")
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
        new("actorId", "string (UUID)", "User who performed the action that produced this event. (DEPRECATED at the top level; prefer auditContext.actorId — kept for back-compat with existing consumers.)")
    ];

    public static readonly EventCatalogPayloadField[] PayloadFields = EnvelopeFields;

    private static readonly EventCatalogPayloadField[] PluginPayloadFields =
    [
        new("pluginId", "string (UUID)", "Identifier of the plugin (matches the plugins table primary key and the on-disk folder name)."),
        new("name", "string", "Plugin display name from its manifest."),
        new("version", "string", "Plugin version from its manifest."),
        new("errorMessage", "string | null", "On `plugin.enable_failed`, the exception message that prevented enable; null on every other plugin event.")
    ];

    private static readonly EventCatalogPayloadField[] AuthPayloadFields =
    [
        new("resourceKind", "string", "Always 'auth' for events on this topic — the bus subject already disambiguates."),
        new("resource", "object | null", "Small payload identifying the actor and (for access.denied) the protected target. Shape varies by event type — see PayloadHighlights below."),
        new("details", "object | null", "Event-specific extras such as the deny reason or per-request counts. See PayloadHighlights below."),
        new("auditContext", "object", "Shared audit context — see the envelope-fields section. Carries the resolved actor, IP, user-agent, request id, route template, and authOutcome.")
    ];

    private static readonly EventCatalogPayloadField[] IamPayloadFields =
    [
        new("resourceKind", "string", "One of 'user', 'group', 'group.member', 'role', 'role.assignment', 'permission.grant', or 'user.supervisor'."),
        new("resource", "object", "Small payload identifying the IAM entity affected. Shape varies by resourceKind — see PayloadHighlights for each event."),
        new("details", "object | null", "Event-specific extras (e.g. selectorString on permission grants, scopeString on role assignments)."),
        new("auditContext", "object", "Shared audit context — actor (the admin who performed the action), IP, user-agent, request id, route template.")
    ];

    private static readonly EventCatalogPayloadField[] RecordSchemaPayloadFields =
    [
        new("resourceKind", "string", "One of 'record-type', 'record-type.field', 'record-edge-type', 'record-edge-type.field', 'record-edge', or 'record.comment'."),
        new("resource", "object", "Small payload identifying the schema or instance affected (e.g. { id, shortCode, name } for record types, { id, recordId, authorId } for comments)."),
        new("details", "object | null", "Event-specific extras. null for most schema events."),
        new("auditContext", "object", "Shared audit context — actor (the admin or user who performed the action), IP, user-agent, request id, route template.")
    ];

    private static readonly EventCatalogPayloadField[] SitePayloadFields =
    [
        new("resourceKind", "string", "One of 'menu', 'menu.item', 'menu.tree', 'settings', 'appearance', or 'status.appearance'."),
        new("resource", "object | null", "Small payload identifying the configuration affected. null for settings.updated (the settings keys are in details)."),
        new("details", "object | null", "Event-specific extras (e.g. { keys, count } on settings.updated, { nodeCount } on menu.tree.replaced)."),
        new("auditContext", "object", "Shared audit context — admin actor who performed the change, IP, user-agent, request id, route template.")
    ];

    private static readonly EventCatalogPayloadField[] WorkflowAdminPayloadFields =
    [
        new("resourceKind", "string", "One of 'workflow.model', 'workflow.execution', or 'workflow.task'."),
        new("resource", "object | null", "Small payload identifying the workflow model, execution (processInstanceId), or task (taskId) affected."),
        new("details", "object | null", "Event-specific extras (e.g. { variableCount, names } on variables.set, { deletedCount } on deleted.all)."),
        new("auditContext", "object", "Shared audit context — actor (admin or task owner), IP, user-agent, request id, route template.")
    ];

    private static readonly EventCatalogPayloadField[] AgentPayloadFields =
    [
        new("resourceKind", "string", "One of 'agent-conversation', 'agent-message', or 'agent-tool-call'."),
        new("resource", "object", "Small payload identifying the conversation, message, or tool call affected. Shape varies by event type — see PayloadHighlights."),
        new("details", "object | null", "Event-specific extras (model id, provider kind, token counts, tool name, duration, error message). Prompt/response text never appears here — auditors get lengths and ids only; the full transcript lives in agent_message."),
        new("auditContext", "object", "Shared audit context — actor (the user driving the chatbot), IP, user-agent, request id, route template.")
    ];

    private static readonly EventCatalogPayloadField[] ExternalConnectionPayloadFields =
    [
        new("resourceKind", "string", "Always 'external-connection' — the bus subject already disambiguates."),
        new("resource", "object | null", "Small payload identifying the connection: { id, kind, name, secretFingerprint }. null on external_connection.list_viewed (filter is in details)."),
        new("details", "object | null", "Event-specific extras (e.g. { hasSecret } on created, { secretChanged } on updated, { ok, latencyMs, modelEcho, error } on tested, { kind, count } on list_viewed). Plaintext api keys never appear."),
        new("auditContext", "object", "Shared audit context — admin actor who performed the action, IP, user-agent, request id, route template.")
    ];

    private static readonly EventCatalogPayloadField[] ContentPayloadFields =
    [
        new("resource.id", "string (UUID)", "Primary key of the affected row (or projectId for membership events)."),
        new("resource.fileName", "string", "Original file name for attachment events; never echoes path components — sanitized at upload."),
        new("resource.contentType", "string", "MIME type recorded at upload; passed through without server-side rewriting."),
        new("resource.sha256Hex", "string (hex)", "Hex-encoded SHA-256 of the attachment bytes, computed at upload time."),
        new("resource.role", "'owner' | 'contributor' | 'viewer'", "Project role for membership events."),
        new("details.fields", "string[] (optional)", "Names of fields that changed on a *.updated event."),
        new("details.newVersionNumber", "integer (optional)", "Version number of the snapshot row created by the change (page/note *.updated, page/note version events)."),
        new("details.locked", "boolean", "New value of deletions_locked for the toggle event.")
    ];

    private static readonly EventCatalogPayloadField[] ViewEventPayloadFields =
    [
        new("resourceKind", "string", "Domain-specific kind of the resource viewed (e.g. 'record', 'iam.user', 'workflow.model'). null/unset for cross-resource list events like list.viewed where there is no single resource."),
        new("resource", "object | null", "Small payload identifying the resource viewed: { id, key/name } for detail views, null for list/search views (the filter is in details)."),
        new("details", "object | null", "Volume-bounded summary metadata: pagination, resultCount, totalCount, filterHash + filterPreview (for searches, capped at 4 KB), or coalesce-window markers for hot polls. NEVER carries the row data itself."),
        new("auditContext", "object", "Shared audit context — actor (the user who viewed), IP, user-agent, request id, route template, authOutcome.")
    ];

    public static readonly EventCatalogTransport[] Transports =
    [
        new(
            BusWatcherStreamService.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "The autonate-flowable-events extension running inside Flowable."),
        new(
            DaprRecordEventPublisher.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the record store after each create / update / archive commit."),
        new(
            DaprApplicationEventPublisher.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from in-app lifecycle events (plugin upload/enable/disable/delete)."),
        new(
            AuthEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the auth surface (login, logout, /me, /check) and from the authorization endpoint filters whenever a request is denied."),
        new(
            IamEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the admin surface whenever a user, group, role, role assignment, permission grant, or supervisor relationship is mutated."),
        new(
            RecordSchemaEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the record-schema surface whenever a record type, record-type field, edge type, edge-type field, edge instance, or record comment is mutated."),
        new(
            SiteEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the site-configuration admin surface whenever menus, settings, branding/appearance, or the per-status color palette are mutated."),
        new(
            WorkflowAdminEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the workflow admin surface for user-initiated commands. Distinct from the system-generated Flowable telemetry on workflow.execution.events."),
        new(
            SystemIssueEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the self-healing platform whenever a system_issues row is opened, escalated, acknowledged, resolved (manual), auto-resolved (machine), or fails remediation."),
        new(
            AgentEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the chatbot agent surface for the conversation lifecycle (create/list/view/rename/delete/compact), each user/assistant message turn, and every tool invocation. Prompt/response text never appears in payloads — only ids, lengths, token counts, and tool names; the full transcript lives in agent_message and the audit log links by id."),
        new(
            ExternalConnectionEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the External Connections admin surface whenever an integration credential is created, edited, deleted, viewed, tested, or set as default for its kind. Plaintext api keys are never carried — only the secret fingerprint (first/last 4 chars + sha256 prefix)."),
        new(
            ContentEventTopic.TopicName,
            "Dapr pub/sub (NATS JetStream in the default deployment). Raw JSON payload, no CloudEvents envelope.",
            "AutoNate.Web — published from the content-hierarchy surface (projects, cabinets, notebooks, pages, page versions, page attachments, notes, note versions) whenever a row is created, edited, moved, archived/restored, or deleted, and whenever a project's membership or deletions-lock changes. Tiptap/Excalidraw/Draw.io payloads and attachment bytes never appear in events — only identifiers, file names, content types, sha256 prefixes, and audit-relevant scalars.")
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
                    ],
                    CarriesRecordType: true),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Updated,
                    "A record's name, assignees, status, due date, or field values changed (or it was unarchived).",
                    "Fires from EfCoreRecordStore.UpdateAsync after commit when at least one field changed, and from SetArchivedAsync when archived flips from true to false.",
                    [
                        "changedFields lists every field touched (e.g. 'name', 'status', 'values.priority').",
                        "When status changed, a separate `record.status.changed` event is also published."
                    ],
                    CarriesRecordType: true),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Deleted,
                    "A record was archived (soft-deleted).",
                    "Fires from EfCoreRecordStore.SetArchivedAsync(archived: true) — the record row stays in the database but is hidden from default reads.",
                    [
                        "isArchived is always true.",
                        "changedFields is ['isArchived']."
                    ],
                    CarriesRecordType: true),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.StatusChanged,
                    "A record's status field changed value.",
                    "Fires from EfCoreRecordStore.UpdateAsync after commit, in addition to `record.updated`, whenever the status differs from its previous value.",
                    [
                        "previousStatus carries the value before the change (may be null).",
                        "status carries the new value (may be null when status was cleared)."
                    ],
                    CarriesRecordType: true),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Restored,
                    "A previously archived record was restored (un-archived).",
                    "Fires from EfCoreRecordStore.SetArchivedAsync(archived: false) after commit. Phase 3 of the audit-events plan made this a distinct event type — previously a restore fired `record.updated`.",
                    [
                        "isArchived is always false.",
                        "changedFields is ['isArchived']."
                    ],
                    CarriesRecordType: true),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Purged,
                    "A record was permanently deleted.",
                    "Fires from EfCoreRecordStore.DeleteAsync after the row is removed. Cascades clean up edges, comments, history, and watches; subscribers maintaining mirrors should drop their copies.",
                    [
                        "isArchived reflects the value at deletion time (often false).",
                        "changedFields is empty."
                    ],
                    CarriesRecordType: true),
                new EventCatalogEntry(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.AssigneesChanged,
                    "A record's assignee list changed.",
                    "Fires from EfCoreRecordStore.UpdateAsync after commit, in addition to `record.updated`, whenever the assigneeIds list differs from its previous value. Phase 3 of the audit-events plan made this a distinct event type.",
                    [
                        "assigneeIds carries the new (current) assignee list.",
                        "changedFields is ['assigneeIds']."
                    ],
                    CarriesRecordType: true)
            ]),
        new(
            "Plugin",
            "Lifecycle events for runtime-loaded plugins — emitted after the plugin management service commits each upload / enable / disable / delete operation.",
            PluginPayloadFields,
            [
                new EventCatalogEntry(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginUploaded,
                    "A new plugin zip has been uploaded and extracted.",
                    "Fires from PluginManagementService.UploadAsync after the plugins-row insert commits. Plugin starts in Disabled status — admin must explicitly enable.",
                    ["errorMessage is null."]),
                new EventCatalogEntry(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginUpdated,
                    "An existing plugin's files have been replaced with a new upload; per-plugin schema/data preserved.",
                    "Fires from PluginManagementService.UpdateAsync after the file swap, manifest fields, and (if it was enabled) re-enable commit. The plugin's id, code, role, and plg_<code> schema are unchanged.",
                    ["errorMessage is null on a clean update; populated when the post-update re-enable failed."]),
                new EventCatalogEntry(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginEnabled,
                    "A plugin has been enabled and its hooks are now live.",
                    "Fires from PluginManagementService.EnableAsync after PluginRuntime successfully loads the assembly and Configure() returns.",
                    ["errorMessage is null."]),
                new EventCatalogEntry(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginDisabled,
                    "A plugin's hooks have been revoked and it is no longer running.",
                    "Fires from PluginManagementService.DisableAsync after the runtime drops the plugin's subscriptions. The assembly stays loaded (inert) until process restart.",
                    ["errorMessage is null."]),
                new EventCatalogEntry(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginDeleted,
                    "A plugin has been deleted from the system.",
                    "Fires from PluginManagementService.DeleteAsync after the row is removed (or marked DeletedPending if files are still locked).",
                    ["errorMessage is null."]),
                new EventCatalogEntry(
                    DaprApplicationEventPublisher.TopicName,
                    ApplicationEventTypes.PluginEnableFailed,
                    "An enable attempt failed (Configure threw, or the assembly could not be loaded).",
                    "Fires from PluginManagementService.EnableAsync when PluginRuntime rejects the load. The plugin's row stays in Disabled status with last_error populated.",
                    ["errorMessage carries the exception message."])
            ]),
        new(
            "Auth",
            "Authentication and authorization events — every login attempt, logout, /me / /check view, and authorization denial. Phase 2 of the audit-events plan.",
            AuthPayloadFields,
            [
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.LoginSucceeded,
                    "A user successfully presented credentials and got a session cookie.",
                    "Fires from /account/login after a successful ValidateCredentialsAsync + SignInAsync. Does not cover the development auto-login middleware.",
                    [
                        "resource: { userId, username }.",
                        "details: { authSource: 'manual' }.",
                        "auditContext.actorId is the newly-authenticated user."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.LoginFailed,
                    "A login attempt was rejected.",
                    "Fires from /account/login when credentials are missing, the password is wrong, or the account is locked. The HTTP 302 redirect to the login page carries an 'error' query param ('invalid' or 'locked') so the SPA can render a message.",
                    [
                        "resource: { username } — the attempted username, may be empty or non-existent.",
                        "details: { reason, failedAttempts } where reason is 'missing_credentials', 'invalid_credentials', or 'account_locked'; failedAttempts is the running counter on the user row (0 when the username is unknown).",
                        "auditContext.actorId is null (no authenticated identity)."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.AccountLocked,
                    "A user's account was locked because the failed-login counter reached the configured threshold.",
                    "Fires from /account/login on the same request that pushed the counter to the threshold, immediately after the matching auth.login.failed event.",
                    [
                        "resource: { userId, username } — the locked account's stable user id (UUID) and username.",
                        "details: { failedAttempts, threshold } — the current counter value (>= threshold) and the configured threshold (default 3).",
                        "auditContext.actorId is null (the user never authenticated)."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.AccountUnlocked,
                    "An admin cleared the lock on a user account.",
                    "Fires from POST /api/users/{id}/unlock after the underlying SetLockedAsync(false) commits.",
                    [
                        "resource: { id, userId, username } — the unlocked account.",
                        "details: null.",
                        "auditContext.actorId is the admin who performed the unlock."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.Logout,
                    "A user signed out — either via the SPA (/api/auth/logout) or by hitting the legacy /account/logout endpoint.",
                    "Fires from both logout endpoints after SignOutAsync.",
                    [
                        "resource: { userId, username } — captured from claims before SignOutAsync clears the principal.",
                        "details: null."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.MeViewed,
                    "An authenticated user fetched their own profile (/api/auth/me) — the SPA does this on every page load. **Coalesced**: at most one event per user per 60-second sliding window so the audit firehose isn't dominated by SPA navigations.",
                    "Fires from AuthEndpoints /me on the authenticated branch only; anonymous probes that return { authenticated: false } don't publish. Suppressed by ViewEventCoalescer when the per-user 60s window is still open.",
                    [
                        "resource: { userId, username }.",
                        "details: { roleCount, groupCount, isSuperAdmin, coalesceWindowSeconds }."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.PermissionChecked,
                    "An authenticated user batched a permission probe via POST /api/auth/check — the SPA uses this to gate action buttons.",
                    "Fires from AuthEndpoints /check after the per-row authorize loop, on authenticated requests only.",
                    [
                        "resource: null.",
                        "details: { checkCount, allowedCount, deniedCount }.",
                        "Per-row outcomes are NOT published — auditors care that the user probed N permissions, not which N they were."
                    ]),
                new EventCatalogEntry(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.AccessDenied,
                    "An authorization filter rejected a request before the endpoint handler ran.",
                    "Fires from RequirePermissionFilter and RequireKindPermissionFilter on every Deny path (including the missing-target-id short-circuit). Single chokepoint for endpoint authz denials.",
                    [
                        "resource: { kind, id, action } — the kind/action pair attempted; id may be '*' for kind-level filters or 'missing_target_id' shorts.",
                        "details: { reason } — human-readable AuthDecision.Reason from the authorizer; for kind-level filters scope='kind' is also set.",
                        "auditContext.authOutcome remains 'Allowed' on the envelope; the deny is in the eventType. (A future enhancement may surface authOutcome='Denied' here once the publisher API supports per-call outcome override.)"
                    ])
            ]),
        new(
            "IAM",
            "Identity and access management mutations — every change to users, groups, roles, role assignments, permission grants, and supervisor relationships. Phase 3 of the audit-events plan.",
            IamPayloadFields,
            [
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.UserCreated,
                    "A new local user was created via the admin API.",
                    "Fires from POST /api/users after ILocalUserStore.CreateAsync commits.",
                    ["resource: { id (long), userId (UUID), username }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.UserUpdated,
                    "A user's username/firstname/lastname/email was updated.",
                    "Fires from PUT /api/users/{id} after ILocalUserStore.UpdateAsync returns a non-null record.",
                    ["resource: { id, userId, username }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.UserPasswordReset,
                    "An admin reset a user's password.",
                    "Fires from POST /api/users/{id}/password after ILocalUserStore.ResetPasswordAsync returns true.",
                    ["resource: { id }. The new password is never carried in the event."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.UserDeleted,
                    "A user was hard-deleted.",
                    "Fires from DELETE /api/users/{id} after ILocalUserStore.DeleteAsync returns true.",
                    ["resource: { id (long) }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.SupervisorSet,
                    "A user's supervisor relationship was set or replaced.",
                    "Fires from PUT /api/users/{userId}/supervisor when the request supplies a non-null supervisorUserId.",
                    ["resource: { userId, supervisorUserId }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.SupervisorCleared,
                    "A user's supervisor relationship was cleared (no replacement).",
                    "Fires from PUT /api/users/{userId}/supervisor when the request body's supervisorUserId is null.",
                    ["resource: { userId, supervisorUserId: null }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupCreated,
                    "A new group was created.",
                    "Fires from POST /api/admin/groups.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupUpdated,
                    "A group's name or description was updated.",
                    "Fires from PATCH /api/admin/groups/{id}.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupArchived,
                    "A group was archived (soft-deleted).",
                    "Fires from POST /api/admin/groups/{id}/archive.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupRestored,
                    "A previously archived group was restored.",
                    "Fires from POST /api/admin/groups/{id}/restore.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupDeleted,
                    "A group was hard-deleted.",
                    "Fires from DELETE /api/admin/groups/{id}.",
                    ["resource: { id }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupMemberAdded,
                    "A user was added to a group.",
                    "Fires from POST /api/admin/groups/{id}/members on success (not on the 'already a member' Conflict path).",
                    ["resource: { groupId, userId }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.GroupMemberRemoved,
                    "A user was removed from a group.",
                    "Fires from DELETE /api/admin/groups/{id}/members/{userId} on success.",
                    ["resource: { groupId, userId }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.RoleCreated,
                    "A new role was created.",
                    "Fires from POST /api/admin/roles.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.RoleUpdated,
                    "A role's name or description was updated.",
                    "Fires from PATCH /api/admin/roles/{id}.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.RoleDeleted,
                    "A role was deleted.",
                    "Fires from DELETE /api/admin/roles/{id} on success — system roles refuse deletion via RoleValidationException.",
                    ["resource: { id }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.RoleAssignmentGranted,
                    "A role was assigned to a principal (user or group).",
                    "Fires from POST /api/admin/roles/{id}/assignments.",
                    [
                        "resource: { id, roleId, principalKind, principalId }.",
                        "details: { scopeString } — the optional scope string limiting where the role applies."
                    ]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.RoleAssignmentRevoked,
                    "A role assignment was revoked.",
                    "Fires from DELETE /api/admin/role-assignments/{id} on success.",
                    ["resource: { id }."]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.PermissionGrantCreated,
                    "A new permission grant was created (allow or deny rule for principal+action+selector).",
                    "Fires from POST /api/admin/grants.",
                    [
                        "resource: { id, principalKind, principalId, action, effect }.",
                        "details: { selectorString, priority }."
                    ]),
                new EventCatalogEntry(
                    IamEventTopic.TopicName, IamEventTypes.PermissionGrantDeleted,
                    "A permission grant was deleted.",
                    "Fires from DELETE /api/admin/grants/{id} on success.",
                    ["resource: { id }."])
            ]),
        new(
            "Record schema",
            "Schema and comment mutations on records — record types and their fields, edge types and edge instances, and record comments. Phase 3 of the audit-events plan.",
            RecordSchemaPayloadFields,
            [
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeCreated,
                    "A new record type (with its short code) was created.",
                    "Fires from POST /api/record-types after IRecordTypeStore.CreateAsync.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeUpdated,
                    "A record type's name/description/icon/color was updated.",
                    "Fires from PATCH /api/record-types/{id}.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeArchived,
                    "A record type was archived (soft-deleted).",
                    "Fires from DELETE /api/record-types/{id}.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeRestored,
                    "A previously archived record type was restored.",
                    "Fires from POST /api/record-types/{id}/restore.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeFieldCreated,
                    "A new field was added to a record type.",
                    "Fires from POST /api/record-types/{id}/fields.",
                    ["resource: { id, recordTypeId, fieldKey, dataType }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeFieldUpdated,
                    "A field's display name/config/required-flag/sort-order was updated.",
                    "Fires from PATCH /api/record-types/{id}/fields/{fieldId}.",
                    ["resource: { id, recordTypeId, fieldKey }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeFieldArchived,
                    "A field was archived (soft-deleted) on a record type.",
                    "Fires from DELETE /api/record-types/{id}/fields/{fieldId}.",
                    ["resource: { id, recordTypeId, fieldKey }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeFieldRestored,
                    "A previously archived field was restored.",
                    "Fires from POST /api/record-types/{id}/fields/{fieldId}/restore.",
                    ["resource: { id, recordTypeId, fieldKey }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeCreated,
                    "A new record-edge type was created.",
                    "Fires from POST /api/record-edge-types.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeUpdated,
                    "A record-edge type's name/inverse/cardinality/etc. was updated.",
                    "Fires from PATCH /api/record-edge-types/{id}.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeArchived,
                    "A record-edge type was archived (soft-deleted).",
                    "Fires from DELETE /api/record-edge-types/{id}.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeRestored,
                    "A previously archived record-edge type was restored.",
                    "Fires from POST /api/record-edge-types/{id}/restore.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeFieldCreated,
                    "A new field was added to a record-edge type.",
                    "Fires from POST /api/record-edge-types/{id}/fields.",
                    ["resource: { id, edgeTypeId, fieldKey, dataType }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeFieldUpdated,
                    "An edge-type field's display name/config/required/sort was updated.",
                    "Fires from PATCH /api/record-edge-types/{id}/fields/{fieldId}.",
                    ["resource: { id, edgeTypeId, fieldKey }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeFieldDeleted,
                    "A field was hard-deleted from a record-edge type (no archive — these are schema-only).",
                    "Fires from DELETE /api/record-edge-types/{id}/fields/{fieldId}.",
                    ["resource: { id, edgeTypeId }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeCreated,
                    "A new record-edge instance was created (one record points to another along an edge type).",
                    "Fires from POST /api/record-edges.",
                    ["resource: { id, edgeTypeId, fromRecordId, toRecordId }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeDeleted,
                    "A record-edge instance was deleted.",
                    "Fires from DELETE /api/record-edges/{id} (always — the store call is fire-and-forget; even non-existent ids publish since the endpoint is idempotent).",
                    ["resource: { id }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordCommentCreated,
                    "A new comment was added to a record.",
                    "Fires from POST /api/records/{recordId}/comments after IRecordCommentStore.CreateAsync commits.",
                    ["resource: { id, recordId, authorId }. The body is NOT carried in the event — auditors can fetch it from the API if needed."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordCommentEdited,
                    "A comment body was edited.",
                    "Fires from PATCH /api/records/{recordId}/comments/{commentId} after EditAsync. Comment body history is preserved in the comment-revisions table.",
                    ["resource: { id, recordId, authorId }."]),
                new EventCatalogEntry(
                    RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordCommentDeleted,
                    "A comment was soft-deleted.",
                    "Fires from DELETE /api/records/{recordId}/comments/{commentId} after SoftDeleteAsync.",
                    ["resource: { id, recordId }."])
            ]),
        new(
            "Site config",
            "Mutations to site-wide configuration: menus, settings, branding/appearance, and per-status colors. Phase 3 of the audit-events plan.",
            SitePayloadFields,
            [
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuCreated,
                    "A new navigation menu (root container) was created.",
                    "Fires from POST /api/admin/menus.",
                    ["resource: { id, key, name }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuUpdated,
                    "A menu's name or description was updated.",
                    "Fires from PATCH /api/admin/menus/{id}.",
                    ["resource: { id, key, name }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuDeleted,
                    "A menu was deleted.",
                    "Fires from DELETE /api/admin/menus/{id} on success.",
                    ["resource: { id }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuItemCreated,
                    "A menu item was added to a menu.",
                    "Fires from POST /api/admin/menus/{key}/items.",
                    ["resource: { id, menuKey, displayName }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuItemUpdated,
                    "A menu item's properties (parent, sort order, display name, icon, item type, config, permission, visibility) were updated.",
                    "Fires from PATCH /api/admin/menus/items/{id}.",
                    ["resource: { id, displayName }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuItemDeleted,
                    "A menu item was deleted.",
                    "Fires from DELETE /api/admin/menus/items/{id} on success.",
                    ["resource: { id }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.MenuTreeReplaced,
                    "An entire menu tree's parent/sort-order graph was replaced in one call.",
                    "Fires from PUT /api/admin/menus/{key}/tree.",
                    [
                        "resource: { menuKey }.",
                        "details: { nodeCount } — number of nodes in the replacement tree."
                    ]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.SettingsUpdated,
                    "One or more site settings were updated in a single call.",
                    "Fires from PATCH /api/admin/site-settings after validation passes for every key.",
                    [
                        "resource: null.",
                        "details: { keys: string[], count } — list of keys that changed; the new values themselves are NOT carried in the event."
                    ]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.AppearanceUpdated,
                    "Site branding/appearance (logo, colors, login background, etc.) was updated.",
                    "Fires from PATCH /api/admin/appearance after SaveChanges.",
                    ["resource: { siteName, logoMode } — header-level identifying info; the full color palette is NOT carried in the event."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.StatusAppearanceCreated,
                    "A new status-color mapping was added (e.g. 'In progress' → #f0ad4e).",
                    "Fires from POST /api/admin/status-appearance.",
                    ["resource: { id, status, color }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.StatusAppearanceUpdated,
                    "A status-color mapping was edited.",
                    "Fires from PATCH /api/admin/status-appearance/{id}.",
                    ["resource: { id, status, color }."]),
                new EventCatalogEntry(
                    SiteEventTopic.TopicName, SiteEventTypes.StatusAppearanceDeleted,
                    "A status-color mapping was deleted.",
                    "Fires from DELETE /api/admin/status-appearance/{id} on success.",
                    ["resource: { id, status }."])
            ]),
        new(
            "Notifications",
            "Notification lifecycle and read-tracking events. Phase 3 of the audit-events plan added the read-tracking events.",
            new EventCatalogPayloadField[]
            {
                new("notificationId", "string (UUID)", "Identifier of the notification (only on notification.created)."),
                new("userId", "string (UUID)", "User who owns the notification."),
                new("kind", "string", "Notification kind discriminator (only on notification.created)."),
                new("title", "string", "Notification title (only on notification.created)."),
                new("body", "string", "Notification body (only on notification.created)."),
                new("relatedEntityKind", "string | null", "Optional kind of the related entity (e.g. 'record')."),
                new("relatedEntityId", "string | null", "Optional id of the related entity."),
                new("linkPath", "string | null", "Optional SPA link path."),
                new("auditContext", "object", "Shared audit context — actor, IP, user-agent, request id, route template.")
            },
            [
                new EventCatalogEntry(
                    DaprNotificationEventPublisher.TopicName, NotificationEventTypes.Created,
                    "A new notification was persisted for a user.",
                    "Fires from DaprNotificationEventPublisher.PublishAsync after the notification row commits.",
                    ["Carries the full notification payload — title, body, relatedEntityKind/id, linkPath."]),
                new EventCatalogEntry(
                    DaprNotificationEventPublisher.TopicName, NotificationEventTypes.Removed,
                    "A previously-persisted notification was deleted because its trigger is no longer actionable.",
                    "Fires from EfCoreNotificationStore.DeleteByRelatedEntityAsync / DeleteByParentEntityAsync when a record is unassigned, a workflow task is completed, or its parent workflow execution is completed/cancelled/deleted.",
                    ["Carries the same payload shape as notification.created so SPA caches can target the row."]),
                new EventCatalogEntry(
                    DaprNotificationEventPublisher.TopicName, NotificationEventTypes.Read,
                    "A user marked a single notification as read.",
                    "Fires from POST /api/notifications/{id}/read on success.",
                    ["resource: { id, userId }."]),
                new EventCatalogEntry(
                    DaprNotificationEventPublisher.TopicName, NotificationEventTypes.AllRead,
                    "A user marked all of their notifications as read in one call.",
                    "Fires from POST /api/notifications/mark-all-read.",
                    ["resource: { userId }. details: { updatedCount }."])
            ]),
        new(
            "Workflow admin",
            "User-initiated workflow commands — saving/publishing/pausing/resuming workflow models, starting executions, and the admin actions on running executions and tasks. Distinct from system-generated Flowable telemetry on workflow.execution.events. Phase 3 of the audit-events plan.",
            WorkflowAdminPayloadFields,
            [
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelSaved,
                    "A workflow model draft was saved.",
                    "Fires from POST /api/workflows after IWorkflowModelStore.SaveAsync.",
                    ["resource: { id, name, processKey }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelPublished,
                    "A workflow model was deployed to Flowable.",
                    "Fires from POST /api/workflows/{id}/publish after Flowable.DeployProcessAsync + store.PublishAsync.",
                    ["resource: { id, name, processKey }. details: { deploymentId, processDefinitionId }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelPaused,
                    "A workflow definition was suspended in Flowable so no new instances start.",
                    "Fires from POST /api/workflows/{id}/pause.",
                    ["resource: { id, name, processKey }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelResumed,
                    "A previously suspended workflow definition was activated.",
                    "Fires from POST /api/workflows/{id}/resume.",
                    ["resource: { id, name, processKey }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelStarted,
                    "A user started a new workflow instance via the API.",
                    "Fires from POST /api/workflows/{processKey}/start after Flowable.StartProcessInstanceAsync. The system-generated process.started event still fires from Flowable on workflow.execution.events.",
                    ["resource: { processKey, processInstanceId, name }. details: { hadVariables }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelDeleted,
                    "A workflow model row was hard-deleted from autonate. Cascades to workflow_model_versions; does NOT undeploy the Flowable-side deployment if the workflow had been published.",
                    "Fires from DELETE /api/workflows/{id} after the row commits.",
                    [
                        "resource: { id, name, processKey } captured pre-delete so consumers can identify what was removed.",
                        "details: { wasPublished, processDefinitionId } — wasPublished is true when LastDeployment was set; processDefinitionId is null for never-published workflows."
                    ]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionVariablesSet,
                    "An admin replaced one or more variables on a running execution (PUT semantics).",
                    "Fires from PUT /api/executions/{processInstanceId}/variables.",
                    ["resource: { processInstanceId }. details: { variableCount, names } — the values themselves are NOT carried."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionVariablesAdded,
                    "An admin added one or more variables to a running execution (POST semantics).",
                    "Fires from POST /api/executions/{processInstanceId}/variables.",
                    ["resource: { processInstanceId }. details: { variableCount, names }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionStateMoved,
                    "An admin moved an execution to a different BPMN activity.",
                    "Fires from POST /api/executions/{processInstanceId}/move-state.",
                    ["resource: { processInstanceId, targetActivityId }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionCancelled,
                    "An admin cancelled a running execution.",
                    "Fires from POST /api/executions/{processInstanceId}/cancel.",
                    ["resource: { processInstanceId }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionDeleted,
                    "An admin deleted an execution.",
                    "Fires from DELETE /api/executions/{processInstanceId}.",
                    ["resource: { processInstanceId }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionsBulkDeleted,
                    "An admin bulk-deleted all executions (used during signal-event debugging).",
                    "Fires from POST /api/executions/delete-all.",
                    ["resource: null. details: { deletedCount }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskForceCompleted,
                    "An admin force-completed a user task on someone else's behalf.",
                    "Fires from POST /api/executions/{processInstanceId}/tasks/{taskId}/force-complete.",
                    ["resource: { processInstanceId, taskId }. details: { hadVariables }."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskReassigned,
                    "An admin reassigned a user task to a different assignee.",
                    "Fires from POST /api/executions/{processInstanceId}/tasks/{taskId}/reassign.",
                    ["resource: { processInstanceId, taskId, assignee } — assignee is the NEW assignee (may be null to unassign)."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskDueDateChanged,
                    "An admin updated a task's due date.",
                    "Fires from POST /api/executions/{processInstanceId}/tasks/{taskId}/due-date.",
                    ["resource: { processInstanceId, taskId, dueDate } — dueDate is the new value (may be null to clear)."]),
                new EventCatalogEntry(
                    WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TaskCompleted,
                    "A user completed their own task (non-admin / non-override path).",
                    "Fires from POST /api/tasks/{taskId}/complete.",
                    ["resource: { taskId }. details: { hadVariables }."])
            ]),
        new(
            "View events (information access)",
            "Read-side audit events. Phase 4 of the audit-events plan instruments every authenticated read endpoint that returns user-facing data, so an audit consumer can answer 'who looked at what, when, from where?' Event-type names follow the convention <domain>.<noun>.viewed (detail), <domain>.<noun>.list.viewed (list), or <domain>.<noun>.searched (search). Hot polls (notifications.unread.count.viewed) are coalesced per user to a 60s window — never sampled. Search-style events carry a filterHash + a 4 KB filterPreview, never the row IDs that came back.",
            ViewEventPayloadFields,
            [
                // Records — published from RecordEndpoints reads.
                new EventCatalogEntry(DaprRecordEventPublisher.TopicName, RecordEventTypes.Viewed,
                    "An authenticated user fetched a single record by id or key.",
                    "Fires from GET /api/records/{id} and /api/records/by-key/{key} on success.",
                    ["resource: { recordId, key, recordTypeId }. by-key calls add details: { lookupBy: 'key' }."]),
                new EventCatalogEntry(DaprRecordEventPublisher.TopicName, RecordEventTypes.ListViewed,
                    "A user listed records (typed list or assigned-to-me).",
                    "Fires from GET /api/records and GET /api/records/assigned-to-me.",
                    ["resource: null. details: { recordTypeId or scope, page, pageSize, resultCount, totalCount, includeArchived, sort }."]),
                new EventCatalogEntry(DaprRecordEventPublisher.TopicName, RecordEventTypes.Searched,
                    "A user ran an advanced record search.",
                    "Fires from POST /api/records/search.",
                    ["details: { recordTypeId, page, pageSize, resultCount, totalCount, filterHash, filterPreview }. The filter object is hashed; raw row data is never carried."]),
                new EventCatalogEntry(DaprRecordEventPublisher.TopicName, RecordEventTypes.HistoryViewed,
                    "A user opened the change history for a record.",
                    "Fires from GET /api/records/{id}/history.",
                    ["resource: { recordId }. details: { fieldKey, take, resultCount }."]),

                // Record schema reads.
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeListViewed,
                    "A user listed record types.", "Fires from GET /api/record-types.",
                    ["details: { resultCount, includeArchived }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeViewed,
                    "A user fetched a single record-type definition.", "Fires from GET /api/record-types/{id}.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeAuditViewed,
                    "A user opened the audit log for a record type.", "Fires from GET /api/record-types/{id}/audit.",
                    ["resource: { recordTypeId }. details: { take, resultCount }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeFieldListViewed,
                    "A user listed fields on a record type.", "Fires from GET /api/record-types/{id}/fields.",
                    ["resource: { recordTypeId }. details: { resultCount, includeArchived }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordTypeFieldViewed,
                    "A user fetched a single record-type field.", "Fires from GET /api/record-types/{id}/fields/{fieldId}.",
                    ["resource: { id, recordTypeId, fieldKey }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeListViewed,
                    "A user listed edge types.", "Fires from GET /api/record-edge-types.",
                    ["details: { resultCount, includeArchived }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeViewed,
                    "A user fetched a single edge-type.", "Fires from GET /api/record-edge-types/{id}.",
                    ["resource: { id, shortCode, name }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTypeFieldListViewed,
                    "A user listed fields on an edge type.", "Fires from GET /api/record-edge-types/{id}/fields.",
                    ["resource: { edgeTypeId }. details: { resultCount }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeListViewed,
                    "A user listed edges for a record.", "Fires from GET /api/records/{id}/edges.",
                    ["resource: { recordId }. details: { direction, edgeTypeId, resultCount }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordEdgeTraversed,
                    "A user ran a graph traversal from a record.", "Fires from POST /api/records/{id}/traverse.",
                    ["resource: { recordId }. details: { startCount, edgeTypeIds, direction, maxHops, resultCount }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordCommentListViewed,
                    "A user opened the comments panel on a record.",
                    "Fires from GET /api/records/{recordId}/comments.",
                    ["resource: { recordId }. details: { resultCount, includeDeleted }."]),
                new EventCatalogEntry(RecordSchemaEventTopic.TopicName, RecordSchemaEventTypes.RecordCommentRevisionsViewed,
                    "A user viewed the edit history of a single comment.",
                    "Fires from GET /api/records/{recordId}/comments/{commentId}/revisions.",
                    ["resource: { id, recordId }. details: { resultCount }."]),

                // IAM reads.
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.UserListViewed,
                    "An admin listed local users.", "Fires from GET /api/users.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.SupervisorsListViewed,
                    "An admin viewed the full supervisor hierarchy.", "Fires from GET /api/users/supervisors.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.SupervisorViewed,
                    "An admin viewed one user's supervisor.", "Fires from GET /api/users/{userId}/supervisor.",
                    ["resource: { userId, supervisorUserId }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.GroupListViewed,
                    "A user listed groups they're authorized to see.",
                    "Fires from GET /api/admin/groups.",
                    ["details: { resultCount, includeArchived }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.GroupViewed,
                    "A user fetched a single group.", "Fires from GET /api/admin/groups/{id}.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.GroupMembersViewed,
                    "A user viewed a group's member list.", "Fires from GET /api/admin/groups/{id}/members.",
                    ["resource: { groupId }. details: { resultCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.RoleListViewed,
                    "A user listed roles.", "Fires from GET /api/admin/roles.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.RoleViewed,
                    "A user fetched a single role.", "Fires from GET /api/admin/roles/{id}.",
                    ["resource: { id, name }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.RoleAssignmentsViewed,
                    "A user viewed assignments for a role.",
                    "Fires from GET /api/admin/roles/{id}/assignments.",
                    ["resource: { roleId }. details: { resultCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.RoleAssignmentsByPrincipalViewed,
                    "A user looked up role assignments for a specific principal.",
                    "Fires from GET /api/admin/role-assignments/by-principal.",
                    ["resource: { principalKind, principalId }. details: { resultCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.PermissionGrantListViewed,
                    "A user listed permission grants (all or scoped to a principal).",
                    "Fires from GET /api/admin/grants.",
                    ["details: { resultCount, scope, principalKind?, principalId? }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.AuthorizationExplained,
                    "An admin ran the effective-permissions debugger for a (user, action, target) triple.",
                    "Fires from POST /api/admin/explain.",
                    ["resource: { asUserId, action, targetKind, targetId }. details: { effect, grantCount }."]),
                new EventCatalogEntry(IamEventTopic.TopicName, IamEventTypes.RegistryViewed,
                    "A user fetched the entity-kind/action/tag registry.",
                    "Fires from GET /api/admin/registry.",
                    ["details: { kindCount }."]),

                // Site config reads.
                new EventCatalogEntry(SiteEventTopic.TopicName, SiteEventTypes.MenuListViewed,
                    "An admin listed all menus.", "Fires from GET /api/admin/menus.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(SiteEventTopic.TopicName, SiteEventTypes.MenuViewed,
                    "A user (or admin) fetched a single menu tree.",
                    "Fires from GET /api/menus/{key} (scope: actor) and GET /api/admin/menus/{key} (scope: admin).",
                    ["resource: { key }. details: { scope: 'actor' | 'admin' }."]),
                new EventCatalogEntry(SiteEventTopic.TopicName, SiteEventTypes.SettingsListViewed,
                    "An admin opened the site-settings page.",
                    "Fires from GET /api/admin/site-settings (the anonymous /api/site-settings is NOT audited — no actor).",
                    ["details: { settingCount }."]),
                new EventCatalogEntry(SiteEventTopic.TopicName, SiteEventTypes.AppearanceViewed,
                    "An admin opened the appearance/branding settings.",
                    "Fires from GET /api/admin/appearance (the anonymous /api/appearance is NOT audited).",
                    ["resource: { siteName, logoMode }."]),
                new EventCatalogEntry(SiteEventTopic.TopicName, SiteEventTypes.StatusAppearanceListViewed,
                    "An admin viewed the per-status color palette.",
                    "Fires from GET /api/admin/status-appearance.",
                    ["details: { resultCount }."]),

                // Workflow admin reads.
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelListViewed,
                    "A user listed workflow models.", "Fires from GET /api/workflows.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelViewed,
                    "A user fetched a single workflow model.", "Fires from GET /api/workflows/{id}.",
                    ["resource: { id, name, processKey }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelLatestViewed,
                    "A user fetched the most recently saved workflow model.",
                    "Fires from GET /api/workflows/latest.",
                    ["resource: { id, name, processKey }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ModelVersionsViewed,
                    "A user viewed the version history for a workflow model.",
                    "Fires from GET /api/workflows/{id}/versions.",
                    ["resource: { id }. details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionListViewed,
                    "A user listed running workflow executions.", "Fires from GET /api/executions.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionDiagramViewed,
                    "A user opened the live diagram for an execution.",
                    "Fires from GET /api/executions/{processInstanceId}/diagram.",
                    ["resource: { processInstanceId }. details: { failedActivityCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionHistoryViewed,
                    "A user opened the activity history of an execution.",
                    "Fires from GET /api/executions/{processInstanceId}/history.",
                    ["resource: { processInstanceId }. details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionLogViewed,
                    "A user opened the chronological log of an execution.",
                    "Fires from GET /api/executions/{processInstanceId}/log.",
                    ["resource: { processInstanceId }. details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionTasksViewed,
                    "A user viewed the current tasks for an execution.",
                    "Fires from GET /api/executions/{processInstanceId}/tasks.",
                    ["resource: { processInstanceId }. details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.ExecutionCompletedAssigneesViewed,
                    "A user looked up the assignees that completed a specific activity.",
                    "Fires from GET /api/executions/{processInstanceId}/activities/{activityId}/completed-assignees.",
                    ["resource: { processInstanceId, activityId }. details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TasksAssignedToMeViewed,
                    "A user viewed their own task inbox.", "Fires from GET /api/tasks/assigned-to-me.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(WorkflowAdminEventTopic.TopicName, WorkflowAdminEventTypes.TasksAssignedToTeamViewed,
                    "A user viewed the inbox of users they supervise (excludes their own tasks).",
                    "Fires from GET /api/tasks/assigned-to-team.",
                    ["details: { resultCount, superviseeCount }."]),

                // Notifications reads (with coalesce on unread-count).
                new EventCatalogEntry(DaprNotificationEventPublisher.TopicName, NotificationEventTypes.ListViewed,
                    "A user opened their notifications dropdown or page.",
                    "Fires from GET /api/notifications.",
                    ["resource: { userId }. details: { resultCount, unreadCount, limit }."]),
                new EventCatalogEntry(DaprNotificationEventPublisher.TopicName, NotificationEventTypes.UnreadCountViewed,
                    "A user's bell icon polled the unread count. **Coalesced**: at most one event per user per 60-second sliding window so the audit firehose isn't dominated by polling.",
                    "Fires from GET /api/notifications/unread-count when the per-user 60s coalesce window has expired.",
                    ["resource: { userId }. details: { unreadCount, coalesceWindowSeconds }."]),

                // Plugins reads.
                new EventCatalogEntry(DaprApplicationEventPublisher.TopicName, ApplicationEventTypes.PluginListViewed,
                    "An admin listed installed plugins.", "Fires from GET /api/admin/plugins.",
                    ["details: { resultCount }."]),
                new EventCatalogEntry(DaprApplicationEventPublisher.TopicName, ApplicationEventTypes.PluginViewed,
                    "An admin fetched a single plugin.", "Fires from GET /api/admin/plugins/{id}.",
                    ["resource: { id, name, version }."])
            ]),

        new(
            "System Issues",
            "Lifecycle events for the self-healing platform. The system_issues table is the source of truth; these events make every state transition observable on the bus so downstream alerters / dashboards / chat-bot integrations can react.",
            EnvelopeFields,
            [
                new EventCatalogEntry(SystemIssueEventTopic.TopicName, SystemIssueEventTypes.Opened,
                    "A detector opened a fresh system_issues row (no existing open issue with the same fingerprint).",
                    "Fires from EfCoreSystemIssueStore.RecordAsync when the upsert resulted in a new insert (occurrence_count == 1).",
                    ["resource: { id, fingerprint, detectorId, category, severity, title }. details: { relatedEntityKind, relatedEntityId, summary }."]),
                new EventCatalogEntry(SystemIssueEventTopic.TopicName, SystemIssueEventTypes.SeverityEscalated,
                    "An open issue's severity changed on a subsequent detector tick (e.g. backlog detector ramps from warning → error as count grows).",
                    "Fires from EfCoreSystemIssueStore.RecordAsync when the upsert hit an existing open/acknowledged row whose severity was different from the incoming one.",
                    ["resource: { id, fingerprint, detectorId, severity, title }. details: { previousSeverity, occurrenceCount }."]),
                new EventCatalogEntry(SystemIssueEventTopic.TopicName, SystemIssueEventTypes.Acknowledged,
                    "An operator acknowledged an issue from the SPA — visible but no longer in the default \"open\" filter.",
                    "Fires from POST /api/system-issues/{id}/acknowledge.",
                    ["resource: { id, fingerprint, severity }. details: { acknowledgedBy }."]),
                new EventCatalogEntry(SystemIssueEventTopic.TopicName, SystemIssueEventTypes.Resolved,
                    "An operator resolved an issue manually with optional notes.",
                    "Fires from POST /api/system-issues/{id}/resolve.",
                    ["resource: { id, fingerprint, severity }. details: { resolvedBy, notes }."]),
                new EventCatalogEntry(SystemIssueEventTopic.TopicName, SystemIssueEventTypes.AutoResolved,
                    "A machine action closed the issue (detector saw the condition clear, or a remediator successfully ran).",
                    "Fires from EfCoreSystemIssueStore.MarkResolvedByFingerprintAsync (detector path) and from SystemIssueRemediationDispatcher on remediator success (Phase 4).",
                    ["resource: { id, fingerprint, severity }. details: { resolutionKind, notes }."]),
                new EventCatalogEntry(SystemIssueEventTopic.TopicName, SystemIssueEventTypes.RemediationFailed,
                    "A remediator attempt failed; if MaxRemediationAttempts is reached the issue stays open for human triage.",
                    "Fires from SystemIssueRemediationDispatcher when an IIssueRemediator throws or returns Failure (Phase 4).",
                    ["resource: { id, fingerprint, detectorId }. details: { attemptCount, maxAttempts, error }."])
            ]),
        new(
            "Agent",
            "Chatbot conversation lifecycle, per-turn message events, and per-tool invocation events. An audit consumer can answer \"what did each user ask the agent, what tools did it call, and did the model finish or error?\" by reading this topic alone — no prompt/response text appears in any payload.",
            AgentPayloadFields,
            [
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ConversationCreated,
                    "A new chatbot conversation row was opened for a user on a specific page.",
                    "Fires from EfCoreAgentConversationStore.CreateAsync after the row commits. Triggered by POST /api/agent/conversations.",
                    ["resource: { id, userId, pageKey }. details: { providerKind, modelId, connectionId }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ConversationViewed,
                    "A user opened a single conversation (loads the full message history).",
                    "Fires from EfCoreAgentConversationStore.GetForUserAsync on the success path. Triggered by GET /api/agent/conversations/{id}.",
                    ["resource: { id, userId }. details: null."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ConversationListViewed,
                    "A user listed their conversations (sidebar / picker).",
                    "Fires from EfCoreAgentConversationStore.ListForUserAsync. Triggered by GET /api/agent/conversations.",
                    ["resource: { userId, pageKey }. details: { count }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ConversationRenamed,
                    "A user renamed a conversation.",
                    "Fires from EfCoreAgentConversationStore.RenameAsync after the row commits. Triggered by PATCH /api/agent/conversations/{id}.",
                    ["resource: { id, userId }. details: { title }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ConversationDeleted,
                    "A user deleted a conversation (cascades to messages and tool calls).",
                    "Fires from EfCoreAgentConversationStore.DeleteAsync after the row commits. Triggered by DELETE /api/agent/conversations/{id}.",
                    ["resource: { id, userId }. details: null."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ConversationCompacted,
                    "The conversation was compacted: an older prefix was replaced with a synthetic assistant summary so subsequent turns stay inside the model's context window.",
                    "Fires from AgentSession after the compactor writes a summary message and the conversation continues with the trimmed history.",
                    ["resource: { id, summaryMessageId }. details: { replacesThroughMessageId, prefixCount, summaryLength }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.MessageUserSent,
                    "A user message was persisted and is about to drive an agent turn.",
                    "Fires from AgentSession.RunAsync immediately after the user message commits.",
                    ["resource: { conversationId, messageId }. details: { length, pageKey, pageSummary } — only the message length, never the text."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.MessageAssistantStarted,
                    "An assistant turn began streaming for a given iteration of the agent loop.",
                    "Fires from AgentSession the first time a chunk is yielded for the iteration's assistant message.",
                    ["resource: { conversationId, messageId, iteration }. details: { providerKind, modelId, contextWindow }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.MessageAssistantCompleted,
                    "An assistant turn finished cleanly (model returned a stop reason).",
                    "Fires from AgentSession after the assistant message and any tool results commit for the iteration.",
                    ["resource: { conversationId, messageId }. details: { stopReason, iteration, inputTokens, outputTokens }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.MessageAssistantFailed,
                    "An assistant turn errored — provider raised, stream broke, or persistence failed.",
                    "Fires from AgentSession's catch block before the loop yields Error/Done.",
                    ["resource: { conversationId, messageId }. details: { error, iteration } — error text is the exception message."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ToolInvoked,
                    "The agent decided to call a tool and the tool-call row was persisted.",
                    "Fires from AgentSession when a tool_use block is materialised, before the tool runs.",
                    ["resource: { conversationId, messageId, toolCallId, toolUseId }. details: { name } — tool args are NOT carried."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ToolCompleted,
                    "A tool invocation returned successfully.",
                    "Fires from AgentSession after the tool's UpdateToolCallAsync commits with a success status.",
                    ["resource: { conversationId, toolCallId, toolUseId }. details: { durationMs, error: null }."]),
                new EventCatalogEntry(AgentEventTopic.TopicName, AgentEventTypes.ToolFailed,
                    "A tool invocation reported an error or threw.",
                    "Fires from AgentSession after the tool's UpdateToolCallAsync commits with a failure status. The loop may continue if the model can recover.",
                    ["resource: { conversationId, toolCallId, toolUseId }. details: { durationMs, error } — error text is the tool's message or exception."])
            ]),
        new(
            "External connections",
            "Outbound integration credentials registered through the External Connections admin surface (LLM provider api keys today; future SMTP/S3/IdP). Mutation events fire post-commit so consumers see only the rows that actually persisted; view events fire on the success path only. Plaintext secrets are never carried — only the fingerprint.",
            ExternalConnectionPayloadFields,
            [
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.Created,
                    "A new external connection (e.g. Anthropic api key) was registered.",
                    "Fires from EfCoreExternalConnectionStore.CreateAsync after the row commits. Triggered by POST /api/admin/external-connections.",
                    ["resource: { id, kind, name, secretFingerprint }. details: { hasSecret }."]),
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.Updated,
                    "An existing connection's metadata or secret was edited.",
                    "Fires from EfCoreExternalConnectionStore.UpdateAsync after the row commits. Triggered by PATCH /api/admin/external-connections/{id}.",
                    ["resource: { id, kind, name, secretFingerprint }. details: { secretChanged } — true when the api key was rotated or cleared."]),
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.Deleted,
                    "A connection was deleted.",
                    "Fires from EfCoreExternalConnectionStore.DeleteAsync after the row commits. Triggered by DELETE /api/admin/external-connections/{id}.",
                    ["resource: { id, kind, name, secretFingerprint } — captured pre-delete so consumers can identify what was removed. details: { actorId }."]),
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.Viewed,
                    "An admin opened a single connection's detail.",
                    "Fires from EfCoreExternalConnectionStore.GetAsync on the success path. Triggered by GET /api/admin/external-connections/{id}.",
                    ["resource: { id, kind, name, secretFingerprint }. details: null."]),
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.ListViewed,
                    "An admin listed connections (optionally filtered by kind).",
                    "Fires from EfCoreExternalConnectionStore.ListAsync. Triggered by GET /api/admin/external-connections.",
                    ["resource: null. details: { kind, count } — kind is the filter applied (null = all kinds)."]),
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.Tested,
                    "An admin clicked the \"Test connection\" button. Fires whether the test succeeded or failed.",
                    "Fires from POST /api/admin/external-connections/{id}/test after ITestConnectionService.TestAsync returns.",
                    ["resource: { id }. details: { ok, latencyMs, modelEcho, error } — error is populated only when ok is false."]),
                new EventCatalogEntry(ExternalConnectionEventTopic.TopicName, ExternalConnectionEventTypes.SetDefault,
                    "An admin marked a connection as the default for its kind (atomic swap inside a transaction).",
                    "Fires from EfCoreExternalConnectionStore.SetDefaultAsync after the transaction commits. Triggered by POST /api/admin/external-connections/{id}/set-default.",
                    ["resource: { id, kind, name, secretFingerprint }. details: null."])
            ]),
        new(
            "Content hierarchy",
            "Mutations and reads across the content hierarchy (project → cabinet → notebook → page → note) plus project membership, page versions, and page attachments. Mutation events fire post-commit. Body / drawing / diagram payloads and attachment bytes never appear in events — only structural identifiers, file metadata, and counts.",
            ContentPayloadFields,
            [
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectCreated,
                    "A project was created. The creator is auto-added as Owner inside the same transaction.",
                    "Fires from POST /api/content/projects after the project + initial owner membership commit.",
                    ["resource: { id, name }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectUpdated,
                    "A project's name / description / is_archived was changed.",
                    "Fires from PATCH /api/content/projects/{id}.",
                    ["resource: { id, name }. details: { fields } — names of fields that changed."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectDeleted,
                    "A project was hard-deleted; cascade-deletes all descendants.",
                    "Fires from DELETE /api/content/projects/{id}. Refused when deletions_locked is true.",
                    ["resource: { id, name }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectDeletionsLockToggled,
                    "An owner toggled the project's deletions_locked flag.",
                    "Fires from PATCH /api/content/projects/{id}/deletions-lock. Owner-only.",
                    ["resource: { id }. details: { locked }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectMemberAdded,
                    "A user was granted a role on a project.",
                    "Fires from PUT /api/content/projects/{id}/members/{userId} when the user had no prior membership.",
                    ["resource: { projectId, userId, role }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectMemberRoleChanged,
                    "An existing member's role was changed.",
                    "Fires from PUT /api/content/projects/{id}/members/{userId} when the row already existed. Refused if it would demote the last owner.",
                    ["resource: { projectId, userId, role }. details: { previousRole }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectMemberRemoved,
                    "A user was removed from a project. Refused if it would remove the last owner.",
                    "Fires from DELETE /api/content/projects/{id}/members/{userId}.",
                    ["resource: { projectId, userId }. details: { previousRole }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CabinetCreated,
                    "A cabinet was created under a project.",
                    "Fires from POST /api/content/cabinets.",
                    ["resource: { id, projectId, name }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CabinetUpdated,
                    "A cabinet was renamed / re-described / re-iconed / re-ordered / archived.",
                    "Fires from PATCH /api/content/cabinets/{id}.",
                    ["resource: { id, name }. details: { fields }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CabinetMoved,
                    "A cabinet was moved to a different project. Closure rows are rebuilt for the subtree.",
                    "Fires from PATCH /api/content/cabinets/{id} when project_id changes.",
                    ["resource: { id }. details: { previousProjectId, newProjectId }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CabinetDeleted,
                    "A cabinet was deleted. Cascade-deletes all descendant notebooks/pages/notes/attachments.",
                    "Fires from DELETE /api/content/cabinets/{id}. Refused when its project has deletions_locked.",
                    ["resource: { id }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NotebookCreated,
                    "A notebook was created under a cabinet.",
                    "Fires from POST /api/content/notebooks.",
                    ["resource: { id, cabinetId, name }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NotebookUpdated,
                    "A notebook's metadata or sort_order changed.",
                    "Fires from PATCH /api/content/notebooks/{id}.",
                    ["resource: { id, name }. details: { fields }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NotebookMoved,
                    "A notebook was moved to a different cabinet.",
                    "Fires from PATCH /api/content/notebooks/{id} when cabinet_id changes.",
                    ["resource: { id }. details: { previousCabinetId, newCabinetId }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NotebookDeleted,
                    "A notebook was deleted (with all pages + notes + attachments).",
                    "Fires from DELETE /api/content/notebooks/{id}. Refused under deletions_locked.",
                    ["resource: { id }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageCreated,
                    "A page was created under a notebook (optionally with a parent page).",
                    "Fires from POST /api/content/pages. The initial body version (v1) is recorded in the same tx.",
                    ["resource: { id, notebookId, parentPageId, title }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageUpdated,
                    "A page was updated. Changes to title/body trigger a version snapshot.",
                    "Fires from PATCH /api/content/pages/{id}.",
                    ["resource: { id, title }. details: { fields, newVersionNumber } — newVersionNumber present only if title/body changed."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageMoved,
                    "A page was moved (different notebook and/or different parent page). Closure is rebuilt.",
                    "Fires from PATCH /api/content/pages/{id} when notebook_id or parent_page_id changes.",
                    ["resource: { id }. details: { previousNotebookId, newNotebookId, previousParentPageId, newParentPageId }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageDeleted,
                    "A page was deleted. Cascade-deletes child pages, notes, attachments, and version rows.",
                    "Fires from DELETE /api/content/pages/{id}. Refused under deletions_locked.",
                    ["resource: { id }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageVersionCreated,
                    "A page version row was written (either an automatic snapshot or a manual save). The current row's body is the *new* state; the version row carries the *prior* state.",
                    "Fires from PATCH /api/content/pages/{id} after a title/body change.",
                    ["resource: { pageId, versionNumber, kind }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageVersionRestored,
                    "A page was reset to a prior version. The current state is first captured as a kind='restore' version.",
                    "Fires from POST /api/content/pages/{id}/versions/{n}/restore.",
                    ["resource: { pageId, restoredFromVersion, snapshotVersionNumber }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageVersionDeleted,
                    "A non-current page version was pruned.",
                    "Fires from DELETE /api/content/pages/{id}/versions/{n}. Refused under deletions_locked, on the current version, or on the only existing version.",
                    ["resource: { pageId, versionNumber }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageAttachmentUploaded,
                    "A binary attachment was added to a page.",
                    "Fires from POST /api/content/pages/{pageId}/attachments after the row commits AND the bytes have been written to the configured store.",
                    ["resource: { id, pageId, fileName, contentType, byteSize, sha256Hex }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageAttachmentRenamed,
                    "An attachment's filename was edited (bytes are immutable).",
                    "Fires from PATCH /api/content/attachments/{id}.",
                    ["resource: { id, pageId, fileName }. details: { previousFileName }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageAttachmentDeleted,
                    "An attachment row was removed. Bytes are deleted best-effort post-commit.",
                    "Fires from DELETE /api/content/attachments/{id}. Refused under deletions_locked.",
                    ["resource: { id, pageId, fileName, contentType, byteSize }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageAttachmentDownloaded,
                    "An attachment was streamed to a caller.",
                    "Fires from GET /api/content/attachments/{id}/download on the success path (after auth and before streaming the bytes).",
                    ["resource: { id, pageId, fileName, byteSize }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NoteCreated,
                    "A note was added to a page.",
                    "Fires from POST /api/content/pages/{pageId}/notes. The initial content version (v1) is recorded in the same tx.",
                    ["resource: { id, pageId, noteKind, title }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NoteUpdated,
                    "A note's title / content / sort_order changed. Changes to title or content trigger a version snapshot.",
                    "Fires from PATCH /api/content/notes/{id}.",
                    ["resource: { id }. details: { fields, newVersionNumber }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NoteDeleted,
                    "A note was deleted. NOT subject to deletions_locked — notes are exempt by design.",
                    "Fires from DELETE /api/content/notes/{id}.",
                    ["resource: { id, pageId }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NoteVersionCreated,
                    "A note version row was written (the snapshot of prior state).",
                    "Fires from PATCH /api/content/notes/{id} after a title/content change.",
                    ["resource: { noteId, versionNumber, kind }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NoteVersionRestored,
                    "A note was reset to a prior version (current is captured as a kind='restore' version first).",
                    "Fires from POST /api/content/notes/{id}/versions/{n}/restore.",
                    ["resource: { noteId, restoredFromVersion, snapshotVersionNumber }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.NoteVersionDeleted,
                    "A non-current note version was pruned. NOT subject to deletions_locked.",
                    "Fires from DELETE /api/content/notes/{id}/versions/{n}.",
                    ["resource: { noteId, versionNumber }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CommentCreated,
                    "A user opened a new comment thread on a page body via the BlockNote editor.",
                    "Fires from POST /api/yjs/comment-event after the Yjs thread write succeeds.",
                    ["resource: { pageId, threadId, commentId }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CommentReplied,
                    "A user added a reply to an existing comment thread.",
                    "Fires from POST /api/yjs/comment-event after the Yjs thread write succeeds.",
                    ["resource: { pageId, threadId, commentId }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CommentResolved,
                    "A comment thread was marked resolved.",
                    "Fires from POST /api/yjs/comment-event after the Yjs thread write succeeds.",
                    ["resource: { pageId, threadId }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CommentReopened,
                    "A previously resolved thread was reopened.",
                    "Fires from POST /api/yjs/comment-event after the Yjs thread write succeeds.",
                    ["resource: { pageId, threadId }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.CommentDeleted,
                    "A comment thread or comment was deleted.",
                    "Fires from POST /api/yjs/comment-event after the Yjs thread write succeeds.",
                    ["resource: { pageId, threadId, commentId? }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.ProjectListViewed,
                    "Caller listed projects visible to them.",
                    "Fires from GET /api/content/projects/page on the success path.",
                    ["resource: null. details: { resultCount, totalCount }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageTreeViewed,
                    "Caller fetched the page tree for a notebook.",
                    "Fires from GET /api/content/notebooks/{id}/page-tree on the success path.",
                    ["resource: { notebookId }. details: { pageCount }."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageFavorited,
                    "Caller marked a page as a favorite for themselves.",
                    "Fires from PUT /api/content/pages/{id}/favorite. Idempotent — re-marking an already-favorited page still publishes.",
                    ["resource: { id, title }. details: null."]),
                new EventCatalogEntry(ContentEventTopic.TopicName, ContentEventTypes.PageUnfavorited,
                    "Caller removed a page from their favorites.",
                    "Fires from DELETE /api/content/pages/{id}/favorite. Idempotent — removing a non-favorited page still publishes.",
                    ["resource: { id, title }. details: null."])
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
    IReadOnlyList<string> PayloadHighlights,
    bool CarriesRecordType = false);
