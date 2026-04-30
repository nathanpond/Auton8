using System.Text.Json;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.BusWatcher;

namespace AutoNate.Web.Services.Notifications;

// Listens to the in-process feed of every Dapr-subscribed topic, filters down
// to Flowable user-task-assigned events, and creates a Notification for the
// new assignee. Lives in-process behind BusWatcherStreamService so it runs
// regardless of which subscriber path delivered the message.
public sealed class WorkflowTaskNotificationListener(
    BusWatcherStreamService busWatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowTaskNotificationListener> logger) : IHostedService
{
    private const string TaskAssignedEventType = "task.assigned";

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

        TaskAssignedPayload? parsed;
        try
        {
            parsed = ParseTaskAssigned(message.Payload);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex,
                "Skipping non-JSON workflow.execution.events payload while looking for task.assigned.");
            return;
        }

        if (parsed is null)
        {
            return;
        }

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
                LinkPath: link),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create workflow.task.assigned notification for user {UserId} (task {TaskId}).",
                assigneeUserId, parsed.TaskId);
        }
    }

    private sealed record class TaskAssignedPayload(
        string? Assignee,
        string? TaskId,
        string? TaskName,
        string? ProcessInstanceId,
        string? ProcessDefinitionName);

    private static TaskAssignedPayload? ParseTaskAssigned(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

        var root = document.RootElement;
        if (!root.TryGetProperty("eventType", out var eventTypeProp)) return null;
        if (eventTypeProp.ValueKind != JsonValueKind.String) return null;
        if (!string.Equals(eventTypeProp.GetString(), TaskAssignedEventType, StringComparison.Ordinal))
        {
            return null;
        }

        return new TaskAssignedPayload(
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
