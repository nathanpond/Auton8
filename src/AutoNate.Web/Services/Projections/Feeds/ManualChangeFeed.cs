using System.Threading.Channels;

namespace AutoNate.Web.Services.Projections.Feeds;

// In-memory feed that admin endpoints and tests can push directly into. The
// worker drains it the same way it drains a NATS feed — handy for forcing
// a single event through the pipeline without standing up a bus.
//
// Singleton-scoped so any caller can resolve it and Enqueue. Capacity-bounded
// to prevent runaway pushers from OOMing the process; over-capacity Enqueues
// throw rather than block (tests get an immediate failure signal).
public sealed class ManualChangeFeed<TSource> : IChangeFeed<TSource>
{
    private readonly Channel<ChangeEvent<TSource>> _channel;

    public ManualChangeFeed(string feedName = "manual")
    {
        FeedName = feedName;
        _channel = Channel.CreateBounded<ChangeEvent<TSource>>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public string FeedName { get; }

    public ValueTask EnqueueAsync(ChangeEvent<TSource> change, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(change, cancellationToken);

    public IAsyncEnumerable<ChangeEvent<TSource>> StreamAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
