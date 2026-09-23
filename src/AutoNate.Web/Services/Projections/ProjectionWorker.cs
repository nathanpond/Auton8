using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Projections;

// Hosts one loop per (projection, feed) pair: pulls ChangeEvents off the
// feed, buffers them up to MaxBatchSize / MaxBatchWindow, and hands the
// batch to the projection inside a single DbContext + transaction. Crashes
// in ApplyAsync are retried with exponential backoff; after MaxAttempts the
// batch is logged and dropped so a poison message can't wedge the feed.
//
// One ProjectionWorker per app. Feeds are discovered via DI by their generic
// type; each projection may have N feeds (typically a push feed for
// freshness + a poll feed for safety) — the worker fans them all out.
public sealed class ProjectionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IProjectionRegistry _registry;
    private readonly IProjectionHealthService _health;
    private readonly IOptions<ProjectionOptions> _options;
    private readonly ILogger<ProjectionWorker> _logger;

    public ProjectionWorker(
        IServiceProvider services,
        IProjectionRegistry registry,
        IProjectionHealthService health,
        IOptions<ProjectionOptions> options,
        ILogger<ProjectionWorker> logger)
    {
        _services = services;
        _registry = registry;
        _health = health;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.WorkerEnabled)
        {
            _logger.LogInformation("ProjectionWorker disabled via Projections:WorkerEnabled.");
            return;
        }

        if (_registry.Projections.Count == 0)
        {
            _logger.LogInformation("ProjectionWorker idling: no projections registered.");
            return;
        }

        // For each projection, look up every feed that targets its source type
        // and launch a drain loop. The loops are independent — one feed
        // failing doesn't take the others down with it.
        var loops = new List<Task>();
        foreach (var projection in _registry.Projections)
        {
            foreach (var loop in StartLoopsForProjection(projection, stoppingToken))
            {
                loops.Add(loop);
            }
        }

        if (loops.Count == 0)
        {
            _logger.LogWarning(
                "ProjectionWorker idling: {Count} projections registered but no feeds matched.",
                _registry.Projections.Count);
            return;
        }

        await Task.WhenAll(loops);
    }

    private IEnumerable<Task> StartLoopsForProjection(IProjection projection, CancellationToken stoppingToken)
    {
        // Resolve IEnumerable<IChangeFeed<TSource>> dynamically.
        var feedInterface = typeof(IChangeFeed<>).MakeGenericType(projection.SourceType);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(feedInterface);
        var feeds = (System.Collections.IEnumerable?)_services.GetService(enumerableType);
        if (feeds is null)
        {
            yield break;
        }

        foreach (var feed in feeds)
        {
            yield return RunLoopAsync(projection, feed!, stoppingToken);
        }
    }

    private async Task RunLoopAsync(IProjection projection, object feed, CancellationToken stoppingToken)
    {
        // Build a typed delegate to call Drain<TSource> via reflection once,
        // then invoke it forever. The reflection happens at startup only.
        // The NonPublic binding flag is intentional — DrainLoopAsync is a
        // private generic-method dispatch path; exposing it would surface
        // an API the framework's own consumers can't usefully call.
#pragma warning disable S3011 // intentional reflection: private generic dispatch
        var drain = typeof(ProjectionWorker)
            .GetMethod(nameof(DrainLoopAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(projection.SourceType);
#pragma warning restore S3011

        try
        {
            await (Task)drain.Invoke(this, new object[] { projection, feed, stoppingToken })!;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Projection {Name} loop exited unexpectedly. The feed will not be restarted until the next app restart.",
                projection.Name);
        }
    }

    // #672. This used to be a bare pass-through: `await foreach` over the feed,
    // flushing only once the buffer reached MaxBatchSize. MaxBatchWindow was
    // configured and documented ("the flush happens implicitly, when the
    // source pauses") but nothing implemented it -- an IAsyncEnumerable's
    // MoveNextAsync just suspends until the next item, there is no such
    // thing as an implicit pause boundary. A feed emitting fewer items per
    // tick than MaxBatchSize (the ordinary case -- a live engine rarely has
    // 250 instances) buffered forever, undelivered, for as long as the app
    // ran. Measured: an instance nothing but a direct write-through (#609)
    // ever wrote a row for -- e.g. one a message flow started on another
    // pool, with nobody calling the read-through for it -- never reached the
    // cache at all, because the only feed that would ever have written it
    // never flushed.
    //
    // The fix keeps a `MoveNextAsync` call alive ACROSS loop iterations
    // (calling it again while a call is still outstanding is not valid on an
    // IAsyncEnumerator) and races it against a deadline that starts ticking
    // the moment the buffer holds its first unflushed item. Reaching
    // MaxBatchSize still flushes immediately, on its own path -- the window
    // never delays a batch that's already full.
    private async Task DrainLoopAsync<TSource>(
        IProjection<TSource> projection,
        IChangeFeed<TSource> feed,
        CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var buffer = new List<ChangeEvent<TSource>>(opts.MaxBatchSize);
        var enumerator = feed.StreamAsync(stoppingToken).GetAsyncEnumerator(stoppingToken);
        await using var disposeEnumerator = enumerator;

        Task<bool>? pendingMoveNext = null;
        DateTime? windowDeadline = null;

        try
        {
            while (true)
            {
                pendingMoveNext ??= enumerator.MoveNextAsync().AsTask();

                if (buffer.Count > 0 && windowDeadline is { } deadline)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        await FlushAsync(projection, feed, buffer, stoppingToken);
                        buffer.Clear();
                        windowDeadline = null;
                        continue;
                    }

                    var winner = await Task.WhenAny(pendingMoveNext, Task.Delay(remaining, stoppingToken));
                    if (winner != pendingMoveNext)
                    {
                        // The window elapsed before another item arrived.
                        // pendingMoveNext is still outstanding -- leave it
                        // set so the next iteration awaits the SAME task
                        // rather than issuing a second, overlapping
                        // MoveNextAsync call.
                        await FlushAsync(projection, feed, buffer, stoppingToken);
                        buffer.Clear();
                        windowDeadline = null;
                        continue;
                    }
                }

                bool hasNext;
                try
                {
                    hasNext = await pendingMoveNext;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                pendingMoveNext = null;
                if (!hasNext)
                {
                    break;
                }

                buffer.Add(enumerator.Current);
                windowDeadline ??= DateTime.UtcNow + opts.MaxBatchWindow;

                if (buffer.Count >= opts.MaxBatchSize)
                {
                    await FlushAsync(projection, feed, buffer, stoppingToken);
                    buffer.Clear();
                    windowDeadline = null;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        if (buffer.Count > 0)
        {
            await FlushAsync(projection, feed, buffer, stoppingToken);
        }
    }

    private async Task FlushAsync<TSource>(
        IProjection<TSource> projection,
        IChangeFeed<TSource> feed,
        IReadOnlyList<ChangeEvent<TSource>> batch,
        CancellationToken stoppingToken)
    {
        // Honor admin pause — events stay in the buffer (well, get dropped
        // here in v1, since the channel was already drained into `batch`).
        // For v1 the trade-off is acceptable: pause is intended for
        // emergency stops, and a small loss while paused is better than the
        // alternative of growing the buffer unboundedly. A v2 could
        // re-enqueue or persist.
        if (_health.IsPaused(projection.Name))
        {
            return;
        }

        var opts = _options.Value;
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var scope = _services.CreateScope();
                var dbFactory = scope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                await projection.ApplyAsync(batch, db, stoppingToken);
                ProjectionMetrics.RecordApplied(projection.Name, feed.FeedName, batch.Count);
                _health.RecordApply(projection.Name, feed.FeedName, batch.Count);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ProjectionMetrics.RecordFailure(projection.Name, feed.FeedName);
                _health.RecordFailure(projection.Name, feed.FeedName, ex.Message);
                if (attempt >= opts.MaxAttempts)
                {
                    _logger.LogError(ex,
                        "Projection {Name} feed {Feed} batch of {Count} dropped after {Attempts} attempts.",
                        projection.Name, feed.FeedName, batch.Count, attempt);
                    return;
                }

                var delay = ComputeBackoff(opts, attempt);
                _logger.LogWarning(ex,
                    "Projection {Name} feed {Feed} batch of {Count} failed on attempt {Attempt}; retrying in {Delay}.",
                    projection.Name, feed.FeedName, batch.Count, attempt, delay);
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { throw; }
            }
        }
    }

    private static TimeSpan ComputeBackoff(ProjectionOptions opts, int attempt)
    {
        var multiplier = 1L << Math.Min(attempt - 1, 20);
        var ticks = opts.BaseRetryDelay.Ticks * multiplier;
        return ticks <= 0 || ticks > opts.MaxRetryDelay.Ticks
            ? opts.MaxRetryDelay
            : TimeSpan.FromTicks(ticks);
    }
}
