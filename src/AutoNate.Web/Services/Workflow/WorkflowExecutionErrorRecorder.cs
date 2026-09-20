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

    public async Task HandleAsync(BusWatcherStreamService.BusWatcherMessage message, CancellationToken cancellationToken = default)
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
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.WorkflowExecutionErrors.Add(row);
            await dbContext.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Records a failure that never became a job, so never became an event (#222).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a second writer exists at all.</b> Everything above is fed by
    /// <c>job.execution.failed</c>, and <b>a step that fails synchronously
    /// produces no job</b> — so it could never reach this table however long you
    /// waited. Measured: a gateway whose condition expression throws takes down
    /// the API call that triggered it, the engine rolls back to the last wait
    /// state, and the instance is left with three history rows and not one of them
    /// errored.
    /// </para>
    /// <para>
    /// <b>The instance surviving is what makes this fixable.</b> On a failing
    /// <i>start</i> the engine rolls the whole thing back and there is no instance
    /// and no history — nothing to attach an error to, and that gap is documented
    /// at the call site rather than papered over with an invented row.
    /// </para>
    /// <para>
    /// <b>The activity is the one the caller acted on, NOT one parsed out of the
    /// engine's message.</b> The message does name the failing element —
    /// <c>activity 'split'</c> — and reading it would be more precise and less
    /// safe: the same string carries the author's own expression text, so a
    /// condition written to contain <c>activity 'something'</c> would attribute
    /// the failure to an element of the author's choosing. <see cref="EngineRefusal"/>
    /// already warns that caller data reaches these strings and parses them only
    /// where it cannot. Attributing to the task the operator completed is both
    /// unspoofable and where they are already looking; the message says the
    /// transition out of it is what failed.
    /// </para>
    /// </remarks>
    public async Task RecordSynchronousFailureAsync(
        string processInstanceId,
        string activityId,
        string message,
        string? stackTrace,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processInstanceId) || string.IsNullOrWhiteSpace(activityId))
        {
            return;
        }

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.WorkflowExecutionErrors.Add(new WorkflowExecutionError
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = processInstanceId,
                ActivityId = activityId,
                ErrorMessage = message,
                ErrorStackTrace = stackTrace,

                // Named so an operator reading the row can tell it apart from one
                // the engine's own event produced -- the two have different
                // reliability, and pretending otherwise would hide that this one
                // exists only because somebody happened to be holding the request.
                RawFlowableEventType = SynchronousEventType,
                OccurredAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Swallowed for the same reason the handler above swallows: the
            // caller is already being told their action failed, and failing to
            // RECORD that is not worth turning into a second, different failure.
            _logger.LogError(
                exception,
                "Failed to record a synchronous workflow failure for processInstanceId={ProcessInstanceId} "
                + "activityId={ActivityId}.",
                processInstanceId,
                activityId);
        }
    }

    /// <summary>What <c>raw_flowable_event_type</c> carries for a row this class synthesised.</summary>
    public const string SynchronousEventType = "autonate.synchronous.failure";

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
