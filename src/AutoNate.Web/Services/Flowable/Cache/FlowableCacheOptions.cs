namespace AutoNate.Web.Services.Flowable.Cache;

public sealed class FlowableCacheOptions
{
    public const string SectionName = "FlowableCache";

    // How often the polling feeds re-query Flowable. 60s is the sweeper
    // safety net cadence; once a Flowable event-bridge feed is wired up,
    // poll frequency can drop without sacrificing freshness.
    public TimeSpan ExecutionPollInterval { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan TaskPollInterval { get; set; } = TimeSpan.FromSeconds(60);

    // Variables are fetched per active instance, so this interval bounds
    // worst-case Flowable load. Longer than the execution interval because
    // running processes don't usually mutate variables every minute and the
    // fetches are the most expensive per-instance call we make.
    public TimeSpan VariablePollInterval { get; set; } = TimeSpan.FromMinutes(5);

    // History event log uses the global Flowable history endpoint, so this
    // interval is cheap. Default to the same cadence as the execution feed.
    public TimeSpan HistoryPollInterval { get; set; } = TimeSpan.FromSeconds(60);

    // Page size for the runtime/tasks REST call when seeding the cache.
    // Larger pages mean fewer round trips during backfill at the cost of
    // bigger response payloads.
    public int TaskPageSize { get; set; } = 200;

    public int HistoryPageSize { get; set; } = 500;

    // Bounded fan-out for the variable feed — fetch at most N instances per
    // tick to keep Flowable load predictable. Instances rotate FIFO by
    // start-time DESC; long-running idle instances will eventually be
    // refreshed but with lower priority than recent starts.
    public int VariableInstancesPerTick { get; set; } = 100;

    // When `last_sync_at` on a cache row is older than this, the cache is
    // considered stale and FlowableReadThrough hits Flowable live before
    // returning the row. Lower bound on user-visible staleness for detail
    // views; AQL queries aren't affected (they always serve from cache).
    public TimeSpan ReadThroughFreshness { get; set; } = TimeSpan.FromSeconds(30);

    public int CurrentProjectionVersion { get; set; } = 1;

    // When the execution projection sees an instance it hasn't cached
    // before, also synchronously fetch that instance's runtime tasks and
    // run them through the task projection — same batch, same DbContext.
    // Without this, CURRENTSTEP() sits blank for up to TaskPollInterval
    // after a new flow appears in the cache. Disable to revert to the
    // pre-fix behavior (independent feeds, ~60s gap).
    public bool CoalesceTasksOnNewInstance { get; set; } = true;

    public bool RetentionEnabled { get; set; } = true;

    // Defaults to ~7 years per the design plan. Per-process overrides are
    // read from the process_retention_config table.
    public int DefaultRetentionDays { get; set; } = 2555;

    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(6);
}
