using System.Text.Json;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.BusWatcher;

namespace AutoNate.Web.Services.Notifications;

// Listens to the in-process feed of every Dapr-subscribed topic, filters down
// to Flowable user-task and process-lifecycle events, and creates or removes
// notifications:
//   - task.assigned         → create a Notification for the new assignee.
//   - task.completed        → remove all Notifications tied to that task id.
//   - process.completed,
//     process.completed.error,
//     process.cancelled     → remove every Notification whose parent is that
//                             process instance (covers in-flight task rows
//                             that never received a task.completed event).
// Lives in-process behind BusWatcherStreamService so it runs regardless of
// which subscriber path delivered the message.
public sealed class WorkflowTaskNotificationListener(
    BusWatcherStreamService busWatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowTaskNotificationListener> logger) : IHostedService
{
    private const string TaskAssignedEventType = "task.assigned";
    private const string TaskCompletedEventType = "task.completed";
    private const string ProcessCompletedEventType = "process.completed";
    private const string ProcessCompletedErrorEventType = "process.completed.error";
    private const string ProcessCancelledEventType = "process.cancelled";

    private readonly BusWatcherStreamService _busWatcher = busWatcher;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<WorkflowTaskNotificationListener> _logger = logger;

    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _busWatcher.Subscribe(HandleAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    private async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!string.Equals(message.Topic, BusWatcherStreamService.TopicName, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            return;
        }

        TaskEventPayload? parsed;
        try
        {
            parsed = ParseTaskEvent(message.Payload);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex,
                "Skipping non-JSON workflow.execution.events payload while looking for task events.");
            return;
        }

        if (parsed is null)
        {
            return;
        }

        switch (parsed.EventType)
        {
            case TaskAssignedEventType:
                await HandleAssignedAsync(parsed);
                break;
            case TaskCompletedEventType:
                await HandleCompletedAsync(parsed);
                break;
            case ProcessCompletedEventType:
            case ProcessCompletedErrorEventType:
            case ProcessCancelledEventType:
                await HandleProcessClosedAsync(parsed);
                break;
        }
    }

    private async Task HandleAssignedAsync(TaskEventPayload parsed)
    {
        if (!Guid.TryParse(parsed.Assignee, out var assigneeUserId))
        {
            // Flowable accepts arbitrary assignee strings (group keys, external
            // ids). Without a way to map back to a local user we can't address
            // a notification — drop these silently.
            return;
        }

        var taskName = string.IsNullOrWhiteSpace(parsed.TaskName)
            ? "(unnamed task)"
            : parsed.TaskName!;
        var processName = string.IsNullOrWhiteSpace(parsed.ProcessDefinitionName)
            ? null
            : parsed.ProcessDefinitionName;
        var body = processName is null
            ? taskName
            : $"{taskName} — {processName}";
        var link = string.IsNullOrWhiteSpace(parsed.ProcessInstanceId)
            ? null
            : $"/executions/{parsed.ProcessInstanceId}";

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
            await store.CreateAsync(new CreateNotificationInput(
                UserId: assigneeUserId,
                Kind: NotificationKinds.WorkflowTaskAssigned,
                Title: "Task assigned to you",
                Body: body,
                RelatedEntityKind: NotificationEntityKinds.WorkflowTask,
                RelatedEntityId: parsed.TaskId,
                LinkPath: link,
                ParentEntityKind: string.IsNullOrWhiteSpace(parsed.ProcessInstanceId)
                    ? null
                    : NotificationEntityKinds.WorkflowExecution,
                ParentEntityId: string.IsNullOrWhiteSpace(parsed.ProcessInstanceId)
                    ? null
                    : parsed.ProcessInstanceId),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create workflow.task.assigned notification for user {UserId} (task {TaskId}).",
                assigneeUserId, parsed.TaskId);
        }
    }

    private async Task HandleCompletedAsync(TaskEventPayload parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.TaskId))
        {
            return;
        }

        // Delete across all users — we don't always know who the active
        // assignee was at completion time (force-complete may have changed it),
        // and any user with an outstanding inbox entry for this task should no
        // longer see it.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
            await store.DeleteByRelatedEntityAsync(
                userId: null,
                relatedEntityKind: NotificationEntityKinds.WorkflowTask,
                relatedEntityId: parsed.TaskId!,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clear workflow-task notifications for completed task {TaskId}.",
                parsed.TaskId);
        }
    }

    private async Task HandleProcessClosedAsync(TaskEventPayload parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.ProcessInstanceId))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
            await store.DeleteByParentEntityAsync(
                parentEntityKind: NotificationEntityKinds.WorkflowExecution,
                parentEntityId: parsed.ProcessInstanceId!,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clear workflow-task notifications for closed process instance {ProcessInstanceId} ({EventType}).",
                parsed.ProcessInstanceId, parsed.EventType);
        }
    }

    private sealed record class TaskEventPayload(
        string EventType,
        string? Assignee,
        string? TaskId,
        string? TaskName,
        string? ProcessInstanceId,
        string? ProcessDefinitionName);

    private static TaskEventPayload? ParseTaskEvent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

        var root = document.RootElement;
        if (!root.TryGetProperty("eventType", out var eventTypeProp)) return null;
        if (eventTypeProp.ValueKind != JsonValueKind.String) return null;
        var eventType = eventTypeProp.GetString();
        if (eventType is not (TaskAssignedEventType
                              or TaskCompletedEventType
                              or ProcessCompletedEventType
                              or ProcessCompletedErrorEventType
                              or ProcessCancelledEventType))
        {
            return null;
        }

        return new TaskEventPayload(
            EventType: eventType!,
            Assignee: GetStringOrNull(root, "assignee"),
            TaskId: GetStringOrNull(root, "taskId"),
            TaskName: GetStringOrNull(root, "taskName"),
            ProcessInstanceId: GetStringOrNull(root, "processInstanceId"),
            ProcessDefinitionName: GetStringOrNull(root, "processDefinitionName"));
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
