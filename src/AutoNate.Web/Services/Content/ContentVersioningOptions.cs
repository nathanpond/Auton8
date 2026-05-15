namespace AutoNate.Web.Services.Content;

// Tunables for the page/note version-history rollup. Read inside the
// snapshot service to decide whether an autosave PATCH should produce a
// fresh version row or fold into the previous session.
public sealed class ContentVersioningOptions
{
    public const string SectionName = "ContentVersioning";

    // Idle window (in minutes) that defines a single editing session for the
    // same author. When the most recent autosave-kind version row is newer
    // than this window AND was written by the same author, a subsequent
    // autosave PATCH updates the live row only — no new history entry.
    // Clamped between 1 minute and 24 hours by the binding code.
    public int SessionGapMinutes { get; set; } = 30;

    public TimeSpan SessionGap => TimeSpan.FromMinutes(
        Math.Clamp(SessionGapMinutes, 1, 24 * 60));
}
