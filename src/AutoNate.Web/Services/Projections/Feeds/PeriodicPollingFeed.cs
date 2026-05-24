using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AutoNate.Web.Services.Projections.Feeds;

// Base for IChangeFeed implementations that poll an external system on a
// timer and emit ChangeEvents. Subclasses implement TickAsync to fetch the
// current page of source rows and call EmitAsync for each one. The base
// owns the timer, cancellation, and the channel that streams events out to
// the ProjectionWorker.
public abstract class PeriodicPollingFeed<TSource> : IChangeFeed<TSource>
{
    private readonly Channel<ChangeEvent<TSource>> _channel;
    private readonly ILogger _logger;

    protected PeriodicPollingFeed(string feedName, TimeSpan interval, ILogger logger)
    {
        FeedName = feedName;
        Interval = interval;
        _logger = logger;
        _channel = Channel.CreateBounded<ChangeEvent<TSource>>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
    }

    public string FeedName { get; }

    public TimeSpan Interval { get; }

    // Called by TickAsync to enqueue an event. Backpressure: bounded channel
    // blocks if the worker is behind, which is the desired behavior (we'd
    // rather pause polling than memory-balloon).
    protected ValueTask EmitAsync(ChangeEvent<TSource> change, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(change, cancellationToken);

    protected abstract Task TickAsync(CancellationToken cancellationToken);

    public async IAsyncEnumerable<ChangeEvent<TSource>> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Run the tick loop on a background task so the consumer can drain
        // _channel concurrently. The tick task exits cleanly on cancellation;
        // any unhandled exception from TickAsync gets logged-and-swallowed
        // so a transient Flowable hiccup doesn't kill the feed permanently.
        var tickTask = Task.Run(async () =>
        {
            try
            {
                await RunTickLoopAsync(cancellationToken);
            }
            finally
            {
                _channel.Writer.TryComplete();
            }
        }, cancellationToken);

        try
        {
            await foreach (var change in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
        finally
        {
            try { await tickTask; } catch { /* logged inside */ }
        }
    }

    private async Task RunTickLoopAsync(CancellationToken cancellationToken)
    {
        // Immediate first tick — populates cache on startup without waiting
        // the full interval, which matters during dev when the user is
        // watching the table fill.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling feed {Feed} tick failed; will retry after Interval.", FeedName);
            }

            try { await Task.Delay(Interval, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
