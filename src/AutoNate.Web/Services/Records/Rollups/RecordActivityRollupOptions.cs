namespace AutoNate.Web.Services.Records.Rollups;

public sealed class RecordActivityRollupOptions
{
    public const string SectionName = "RecordActivityRollup";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(1);

    // Only recompute the last N days on each tick. Older days are stable
    // (records aren't normally backdated) so recomputing them every hour is
    // wasted work — they get caught by a separate full-rebuild on demand.
    public int RecentDayWindow { get; set; } = 7;

    public int CurrentProjectionVersion { get; set; } = 1;
}
