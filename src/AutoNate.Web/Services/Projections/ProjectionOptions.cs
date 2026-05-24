namespace AutoNate.Web.Services.Projections;

public sealed class ProjectionOptions
{
    public const string SectionName = "Projections";

    // Max events batched into a single ApplyAsync call. Larger batches
    // amortize transaction overhead but increase head-of-line latency for
    // any single event. 250 is a starting point tuned for Flowable's typical
    // event burst on a process start (one PROCESS_STARTED + ~5–10 task/var
    // events arriving within ~50ms).
    public int MaxBatchSize { get; set; } = 250;

    // Maximum time we'll buffer before flushing a partial batch. The lower
    // bound on visible cache lag (per event) is roughly this value + the
    // ApplyAsync transaction duration.
    public TimeSpan MaxBatchWindow { get; set; } = TimeSpan.FromMilliseconds(500);

    // When ApplyAsync throws, we requeue the batch with exponential backoff
    // bounded by these limits. Catastrophic per-row failures get logged once
    // and skipped after MaxAttempts so the feed never wedges.
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 20;

    // When false, ProjectionWorker.ExecuteAsync returns immediately without
    // starting any drain loops. Used by tests so a process under xUnit
    // parallelism doesn't spin up dozens of background polling feeds across
    // simultaneous test factories.
    public bool WorkerEnabled { get; set; } = true;
}
