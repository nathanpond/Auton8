using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.BusWatcher;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

// Listens to the workflow telemetry stream and persists a row for every
// job.execution.failed event so the executions UI can render the failed
// node in red and surface an "Errored" status. Subscribing in-process
// (via BusWatcherStreamService.Subscribe) keeps us off the Dapr message
// path — DaprStreamingSubscriber already feeds this stream.
public sealed class WorkflowExecutionErrorRecorder(
    BusWatcherStreamService busWatcher,
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<WorkflowExecutionErrorRecorder> logger) : IHostedService
{
    private const string FailedEventType = "job.execution.failed";

    private readonly BusWatcherStreamService _busWatcher = busWatcher;
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<WorkflowExecutionErrorRecorder> _logger = logger;

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

        WorkflowExecutionError? row;
        try
        {
            row = TryBuildRow(message.Payload);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "WorkflowExecutionErrorRecorder could not parse payload on topic {Topic}.", message.Topic);
            return;
        }

        if (row is null)
        {
            return;
        }

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            dbContext.WorkflowExecutionErrors.Add(row);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to persist workflow execution error for processInstanceId={ProcessInstanceId} activityId={ActivityId}.",
                row.ProcessInstanceId,
                row.ActivityId);
        }
    }

    private static WorkflowExecutionError? TryBuildRow(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var eventType = ReadString(root, "eventType");
        if (!string.Equals(eventType, FailedEventType, StringComparison.Ordinal))
        {
            return null;
        }

        var processInstanceId = ReadString(root, "processInstanceId");
        var activityId = ReadString(root, "activityId");
        if (string.IsNullOrWhiteSpace(processInstanceId) || string.IsNullOrWhiteSpace(activityId))
        {
            return null;
        }

        return new WorkflowExecutionError
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = processInstanceId,
            ActivityId = activityId,
            ActivityName = ReadString(root, "activityName"),
            ErrorMessage = ReadString(root, "errorMessage"),
            ErrorStackTrace = ReadString(root, "errorStackTrace"),
            RawFlowableEventType = ReadString(root, "rawFlowableEventType"),
            OccurredAtUtc = ReadDateTime(root, "occurredAtUtc") ?? DateTime.UtcNow
        };
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTime? ReadDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return element.TryGetDateTime(out var value) ? value.ToUniversalTime() : null;
    }
}
