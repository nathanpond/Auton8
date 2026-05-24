namespace AutoNate.Web.Services.Flowable.Cache.ColdTier;

public sealed class ColdTierOptions
{
    public const string SectionName = "FlowableCache:ColdTier";

    // When true, ColdTierArchiverService loops on ArchiveSweepInterval and
    // moves rows older than ArchiveAfterDays out of workflow_event_log_cache
    // and into per-month Parquet files. When false, the service exits cleanly
    // at startup — workflow_event_log_cache keeps everything forever
    // (matching pre-Phase-3 behavior).
    public bool Enabled { get; set; } = false;

    public TimeSpan ArchiveSweepInterval { get; set; } = TimeSpan.FromHours(24);

    public int ArchiveAfterDays { get; set; } = 90;

    // Filesystem layout: ${Root}/workflow_event_log/{YYYY}-{MM}.parquet.
    // Defaults to a path relative to the app's working directory; production
    // deploys should pin this to a persistent volume.
    public string Root { get; set; } = "var/projections";

    // Hard cap on rows pulled into one Parquet write. Defends against an
    // operator running with stale retention config: we'd rather take many
    // archive passes than build a multi-GB Parquet file in one shot.
    public int MaxRowsPerArchivePass { get; set; } = 500_000;

    // Optional safety net for tests / fresh dev DBs: never archive rows
    // newer than this threshold even if ArchiveAfterDays would otherwise
    // allow it. Defaults to 1 day so a clock drift never strands recent
    // events in Parquet.
    public TimeSpan MinimumRowAge { get; set; } = TimeSpan.FromDays(1);
}
