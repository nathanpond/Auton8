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

public interface IPipelineRunStore
{
    Task<PipelineRun> EnqueueAsync(
        Guid pipelineId,
        string graphSnapshotJson,
        Guid actorId,
        string triggerKind,
        CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRunStep>> ListStepsAsync(
        Guid runId, CancellationToken cancellationToken = default);
}
