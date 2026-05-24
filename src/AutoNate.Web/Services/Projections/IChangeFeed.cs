namespace AutoNate.Web.Services.Projections;

// A source of change events for a projection. Implementations are responsible
// for whatever the underlying transport requires (NATS subscription, REST
// polling, manual triggers) and emit a continuous IAsyncEnumerable that the
// ProjectionWorker drains until cancellation.
//
// Multiple feeds may serve the same projection (e.g., a NATS push feed for
// freshness + a polling sweeper feed for safety). The projection's
// idempotency contract handles the overlap.
public interface IChangeFeed<TSource>
{
    // Stable name used in logs/metrics. Two feeds for the same projection
    // SHOULD have distinct feed names (e.g. "flowable.exec.nats" and
    // "flowable.exec.sweeper") so per-feed lag is observable.
    string FeedName { get; }

    IAsyncEnumerable<ChangeEvent<TSource>> StreamAsync(CancellationToken cancellationToken);
}
