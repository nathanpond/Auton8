using System.Text.Json;
using AutoNate.Web.Authorization;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Maps a workflow.execution.events message to:
//   workflow-execution:{processInstanceId}    — instance, IAuthorizer gated
//   workflow-executions:visible               — list, IAuthorizer gated
//   workflow-task:{taskId}                    — only if eventType starts task.*
//   workflow-tasks:assigned-to:{userId}       — only if task + assignee Guid
//   tasks:assigned-to:{userId}                — same, broader "my tasks" channel
//   tasks:supervisees-of-me                   — one channel that every
//                                                supervisor self-subscribes to;
//                                                per-connection FastGate via
//                                                snapshot.Supervises(assignee).
//
// Note: the plan originally specified per-supervisor channel names
// (`tasks:supervisees-of:{supervisorId}`), but that requires a reverse-edge
// DB query per task event to enumerate the assignee's supervisors. The single
// channel-name + FastGate variant uses the snapshot's already-loaded outbound
// edges and avoids the per-message DB hit. Per-event cost is
// O(supervisor-subscribers) instead of O(supervisors-of-assignee), comparable
// in practice because supervisor cohorts are small.
//
// Flowable's assignee field is loose (group keys, external ids, etc.) — we
// only fan out the per-user channels when it parses as a Guid.
public sealed class WorkflowChannelResolver : IChannelResolver
{
    public string Topic => BusWatcherStreamService.TopicName;

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!TryExtract(message.Payload, out var fields))
        {
            return Array.Empty<ResolvedDelivery>();
        }

        var deliveries = new List<ResolvedDelivery>(4);

        if (!string.IsNullOrEmpty(fields.ProcessInstanceId))
        {
            var executionTarget = new EntityRef(EntityKinds.WorkflowExecution, fields.ProcessInstanceId);
            deliveries.Add(new ResolvedDelivery(
                $"{WorkflowChannelNames.ExecutionInstanceKind}:{fields.ProcessInstanceId}",
                executionTarget, FastGate: null));
            deliveries.Add(new ResolvedDelivery(
                WorkflowChannelNames.ExecutionsVisibleList,
                executionTarget, FastGate: null));
        }

        var isTaskEvent = fields.EventType is not null
            && fields.EventType.StartsWith("task.", StringComparison.Ordinal);
        if (isTaskEvent && !string.IsNullOrEmpty(fields.TaskId))
        {
            var taskTarget = new EntityRef(EntityKinds.WorkflowTask, fields.TaskId);
            deliveries.Add(new ResolvedDelivery(
                $"{WorkflowChannelNames.TaskInstanceKind}:{fields.TaskId}",
                taskTarget, FastGate: null));

            if (Guid.TryParse(fields.Assignee, out var assigneeUserId))
            {
                deliveries.Add(new ResolvedDelivery(
                    $"{WorkflowChannelNames.TasksListKind}:assigned-to:{assigneeUserId}",
                    taskTarget, FastGate: null));
                deliveries.Add(new ResolvedDelivery(
                    $"{WorkflowChannelNames.MyTasksListKind}:assigned-to:{assigneeUserId}",
                    taskTarget, FastGate: null));

                // Single supervisees-of channel — every supervisor self-
                // subscribes; FastGate checks whether this specific assignee
                // is one of theirs.
                var assigneeUserIdString = assigneeUserId.ToString();
                deliveries.Add(new ResolvedDelivery(
                    WorkflowChannelNames.SuperviseesOfMe,
                    taskTarget,
                    snapshot => snapshot.Supervises(assigneeUserIdString)));
            }
        }

        return deliveries;
    }

    private readonly record struct ParsedFields(
        string? EventType,
        string? ProcessInstanceId,
        string? TaskId,
        string? Assignee);

    private static bool TryExtract(string payload, out ParsedFields fields)
    {
        fields = default;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            fields = new ParsedFields(
                EventType: GetString(document.RootElement, "eventType"),
                ProcessInstanceId: GetString(document.RootElement, "processInstanceId"),
                TaskId: GetString(document.RootElement, "taskId"),
                Assignee: GetString(document.RootElement, "assignee"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}

public static class WorkflowChannelNames
{
    public const string ExecutionInstanceKind = "workflow-execution";
    public const string TaskInstanceKind = "workflow-task";
    public const string ExecutionsListKind = "workflow-executions";
    public const string TasksListKind = "workflow-tasks";
    public const string MyTasksListKind = "tasks";

    public const string ExecutionsVisibleList = "workflow-executions:visible";
    public const string SuperviseesOfMe = "tasks:supervisees-of-me";
}
