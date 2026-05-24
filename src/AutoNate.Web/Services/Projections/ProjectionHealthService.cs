using System.Collections.Concurrent;

namespace AutoNate.Web.Services.Projections;

public sealed class ProjectionHealthService : IProjectionHealthService
{
    private readonly ConcurrentDictionary<string, ProjectionRuntimeState> _projections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FeedRuntimeState> _feeds = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPaused(string projectionName) =>
        _projections.GetValueOrDefault(projectionName)?.Paused ?? false;

    public void Pause(string projectionName) => Get(projectionName).Paused = true;

    public void Resume(string projectionName) => Get(projectionName).Paused = false;

    public void RecordApply(string projectionName, string feedName, int eventCount)
    {
        var state = Get(projectionName);
        Interlocked.Add(ref state.EventsAppliedSinceStart, eventCount);
        Interlocked.Add(ref state.EventsAppliedTotal, eventCount);
        state.LastAppliedAtUtc = DateTimeOffset.UtcNow;

        // Per-feed counters mirror per-projection so a noisy feed shows up
        // distinctly from its siblings on the admin page.
        var feed = GetFeed(feedName);
        Interlocked.Add(ref feed.EventsObservedTotal, eventCount);
        feed.LastEventObservedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(string projectionName, string feedName, string message)
    {
        var state = Get(projectionName);
        Interlocked.Increment(ref state.ApplyFailuresTotal);
        state.LastFailureAtUtc = DateTimeOffset.UtcNow;
        state.LastFailureMessage = message;
    }

    public void RecordFeedObservation(string feedName, int eventCount)
    {
        var feed = GetFeed(feedName);
        Interlocked.Add(ref feed.EventsObservedTotal, eventCount);
        feed.LastEventObservedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RecordWatermark(string feedName, DateTimeOffset watermark) =>
        GetFeed(feedName).WatermarkUtc = watermark;

    public IReadOnlyList<ProjectionHealthSnapshot> Snapshot(IEnumerable<IProjection> projections) =>
        projections.Select(p => Snapshot(p)!).ToList();

    public ProjectionHealthSnapshot? Snapshot(IProjection projection)
    {
        var state = _projections.GetValueOrDefault(projection.Name) ?? new ProjectionRuntimeState();

        // Feed health is keyed by feed name globally — projections sharing a
        // source type each see the same feed entries, which matches reality
        // (one feed instance fans out to every projection that wants its
        // source). The admin page renders the per-feed columns alongside
        // each projection it serves.
        var feedNames = ResolveFeedNames(projection);
        var feeds = feedNames
            .Select(name =>
            {
                var f = _feeds.GetValueOrDefault(name) ?? new FeedRuntimeState();
                return new ProjectionFeedHealth(
                    name,
                    Interlocked.Read(ref f.EventsObservedTotal),
                    f.LastEventObservedAtUtc,
                    f.WatermarkUtc);
            })
            .ToList();

        return new ProjectionHealthSnapshot(
            projection.Name,
            projection.Version,
            projection.SourceType.FullName ?? projection.SourceType.Name,
            state.Paused,
            Interlocked.Read(ref state.EventsAppliedTotal),
            Interlocked.Read(ref state.EventsAppliedSinceStart),
            Interlocked.Read(ref state.ApplyFailuresTotal),
            state.LastAppliedAtUtc,
            state.LastFailureAtUtc,
            state.LastFailureMessage,
            feeds);
    }

    // Best-effort: feed names follow the convention `<source-area>.<aspect>.<mode>`
    // (e.g. `flowable.exec.poll`). The health service has no DI access to the
    // feed registry from this scope, so we surface feeds that have actually
    // emitted at least once. Feeds that have never fired don't appear; the
    // admin page treats their absence as "feed hasn't started yet."
    private IReadOnlyList<string> ResolveFeedNames(IProjection projection) =>
        _feeds.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    private ProjectionRuntimeState Get(string name) =>
        _projections.GetOrAdd(name, _ => new ProjectionRuntimeState());

    private FeedRuntimeState GetFeed(string name) =>
        _feeds.GetOrAdd(name, _ => new FeedRuntimeState());

    private sealed class ProjectionRuntimeState
    {
        public bool Paused;
        public long EventsAppliedSinceStart;
        public long EventsAppliedTotal;
        public long ApplyFailuresTotal;
        public DateTimeOffset? LastAppliedAtUtc;
        public DateTimeOffset? LastFailureAtUtc;
        public string? LastFailureMessage;
    }

    private sealed class FeedRuntimeState
    {
        public long EventsObservedTotal;
        public DateTimeOffset? LastEventObservedAtUtc;
        public DateTimeOffset? WatermarkUtc;
    }
}
