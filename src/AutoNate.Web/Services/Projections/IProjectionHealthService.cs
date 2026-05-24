namespace AutoNate.Web.Services.Projections;

// Single-process state holder for projection runtime telemetry + admin
// controls (pause/resume). Thread-safe — accessed by the ProjectionWorker
// (writes), admin endpoints (reads + flips), and Prometheus scrape (reads).
//
// Multi-process deployments converge on the same row state by querying the
// projection_versions / projection_watermarks tables; the in-memory portion
// here is per-instance and only authoritative for "what did THIS process
// just do." Cross-instance pause signaling would need a shared channel
// (Dapr, Redis pubsub) — out of scope for v1 and easy to add later.
public interface IProjectionHealthService
{
    bool IsPaused(string projectionName);

    void Pause(string projectionName);

    void Resume(string projectionName);

    void RecordApply(string projectionName, string feedName, int eventCount);

    void RecordFailure(string projectionName, string feedName, string message);

    void RecordFeedObservation(string feedName, int eventCount);

    void RecordWatermark(string feedName, DateTimeOffset watermark);

    IReadOnlyList<ProjectionHealthSnapshot> Snapshot(IEnumerable<IProjection> projections);

    ProjectionHealthSnapshot? Snapshot(IProjection projection);
}
