using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Pipelines;

public sealed class PipelineRunNotFoundException(Guid id)
    : Exception($"Pipeline run '{id}' was not found.");

public static class PipelineRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class PipelineRunTriggerKinds
{
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
}

// Step log entry (audit fix archived-11). The orchestrator buffers these
// during each step's execution and persists the buffer alongside the
// step's terminal status. Level is a free string ("info" / "warn" /
// "error") so plugin runners can use whatever vocabulary they want;
// the SPA color-codes a known set and falls back to dimmed text.
public sealed record class PipelineRunStepLog(
    DateTime TimestampUtc,
    string Level,
    string Message);

// Outcome of a user-initiated cancel request. Distinguishes the
// no-op cases so the API layer can return the right status code
// without bespoke per-state branching.
public enum RunCancellationResult
{
    NotFound,
    AlreadyTerminal,
    Cancelled,
}

public interface IPipelineRunStore
{
    Task<PipelineRun> EnqueueAsync(
        Guid pipelineId,
        string graphSnapshotJson,
        Guid actorId,
        string triggerKind,
        CancellationToken cancellationToken = default);

    // External cancel request — flips Queued or Running runs to
    // Cancelled and stamps CompletedAtUtc. The worker's poll already
    // filters Queued so a cancelled-Queued run is never picked up; the
    // orchestrator's between-node check (in PipelineOrchestrator) reads
    // the row and bails when it sees Cancelled, finishing whatever node
    // is mid-flight first (graceful cancel; mid-node interrupt is a
    // future revision that would thread a per-run CTS).
    Task<RunCancellationResult> RequestCancellationAsync(
        Guid runId, DateTime utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRun>> DequeueOldestAsync(
        int limit, CancellationToken cancellationToken = default);

    Task MarkRunningAsync(Guid runId, DateTime utcNow, CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        Guid runId,
        string status,
        string? errorMessage,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<PipelineRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRun>> ListForPipelineAsync(
        Guid pipelineId, int limit, CancellationToken cancellationToken = default);

    // Per-step lifecycle.
    Task<PipelineRunStep> CreateStepAsync(
        Guid runId,
        string nodeKey,
        string nodeKind,
        CancellationToken cancellationToken = default);

    Task MarkStepStartedAsync(Guid stepId, DateTime utcNow, CancellationToken cancellationToken = default);

    Task MarkStepCompletedAsync(
        Guid stepId,
        string status,
        long? rowCount,
        string? errorMessage,
        DateTime utcNow,
        // Buffered log entries to persist alongside the terminal status.
        // Empty by default so existing callers compile without churn.
        IReadOnlyList<PipelineRunStepLog>? logs = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRunStep>> ListStepsAsync(
        Guid runId, CancellationToken cancellationToken = default);
}
