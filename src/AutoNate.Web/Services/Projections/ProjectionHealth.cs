namespace AutoNate.Web.Services.Projections;

// Snapshot of a single projection's runtime state. The health service mints
// one of these per registered projection on every snapshot request.
public sealed record ProjectionHealthSnapshot(
    string Name,
    int Version,
    string SourceType,
    bool Paused,
    long EventsAppliedTotal,
    long EventsAppliedSinceStart,
    long ApplyFailuresTotal,
    DateTimeOffset? LastAppliedAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastFailureMessage,
    IReadOnlyList<ProjectionFeedHealth> Feeds);

public sealed record ProjectionFeedHealth(
    string FeedName,
    long EventsObservedTotal,
    DateTimeOffset? LastEventObservedAtUtc,
    DateTimeOffset? WatermarkUtc);

// Status returned by admin actions so the SPA / curl caller knows whether a
// rebuild kicked off, a pause flag flipped, etc.
public sealed record ProjectionActionResult(
    bool Ok,
    string Message,
    ProjectionHealthSnapshot? Snapshot = null);
