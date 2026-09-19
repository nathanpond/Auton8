namespace AutoNate.Web.Services.Flowable.Cache;

public sealed class FlowableCacheOptions
{
    public const string SectionName = "FlowableCache";

    // How often the polling feeds re-query Flowable. 60s is the sweeper
    // safety net cadence; once a Flowable event-bridge feed is wired up,
    // poll frequency can drop without sacrificing freshness.
    public TimeSpan ExecutionPollInterval { get; set; } = TimeSpan.FromSeconds(60);

    // #588. How many pages of each execution collection one POLL tick may fetch.
    //
    // The poll used to fetch a single fixed page of 200 with no paging at all,
    // which put a hard ceiling on what the cache could ever hold -- the list
    // reading it could not show more however well its query was written. Paging
    // without a ceiling would swap that for an unbounded tick that re-walks all
    // of history every 60 seconds as history grows.
    //
    // Five pages is 1000 instances per tick: enough that the cap stops being the
    // thing a user notices, small enough that a tick stays predictable. The
    // BACKFILL is the path with no ceiling -- see ExecutionBackfillMaxPages --
    // because it is a one-shot operator action, which is exactly the division the
    // projection framework's design already assumes.
    public int ExecutionPollMaxPages { get; set; } = 5;

    // #588. The backfill's ceiling. Deliberately large rather than unbounded: a
    // runaway loop against an engine with a pathological dataset should end, and
    // a number that can be raised in configuration is better than one that cannot.
    public int ExecutionBackfillMaxPages { get; set; } = 10_000;

    // #594. How many missed poll intervals before the executions view reports
    // "not updating" rather than merely stale.
    //
    // Three, deliberately: one missed tick is a hiccup -- a slow Flowable, a
    // redeploy, a GC pause -- and three is a pattern. Telling a user the system
    // has stopped updating when it has merely been slow once is the cry-wolf that
    // makes the indicator ignored, and the indicator exists for the operator
    // watching a stuck process.
    //
    // Configuration rather than a constant so an operator who disagrees can change
    // it without a release. At the 60s default poll this is a three-minute window.
    public int StaleFeedIntervalMultiplier { get; set; } = 3;

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

    // #583. The execution cache versions independently of the other three.
    //
    // CurrentProjectionVersion is shared by the execution, task, history and
    // variable projections, so bumping it to mark a change in ONE of them
    // relabels the other three, whose shape did not change. It is also never
    // compared anywhere -- nothing re-projects on a version change; BackfillRunner
    // says as much ("the shadow-rename path will land when the first version bump
    // is needed in anger"). So the shared field cannot carry this meaning.
    //
    // This one does, and carries exactly one: a version-2 row was written by code
    // that knows about `name`. That is what lets a null name be read as "this run
    // has no name" rather than "this row predates the column".
    public int ExecutionProjectionVersion { get; set; } = 2;

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
