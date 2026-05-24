namespace AutoNate.Web.Services.Projections;

// Per-feed watermark for pull/sweep feeds. NATS feeds don't use this — JetStream
// owns their cursor — but the polling sweeper writes its lastUpdateTime here
// so a restart doesn't replay the entire history.
public interface IProjectionWatermarkStore
{
    Task<DateTimeOffset?> GetAsync(string feedName, CancellationToken cancellationToken);

    Task SetAsync(string feedName, DateTimeOffset watermark, CancellationToken cancellationToken);
}
