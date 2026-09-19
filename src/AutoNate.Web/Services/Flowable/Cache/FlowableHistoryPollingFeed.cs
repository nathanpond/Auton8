using AutoNate.Web.Models;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Pages the global Flowable historic-activity-instances endpoint NEWEST FIRST
// and stops at the first event older than the watermark (#590).
//
// It used to page ascending with a `startedAfter` the server ignores, which
// meant it replayed the entire history every tick while looking incremental.
// The append-only projection makes the deliberate boundary overlap harmless.
public sealed class FlowableHistoryPollingFeed : PeriodicPollingFeed<FlowableHistoricActivityEvent>
{
    private readonly IFlowableClient _flowable;
    private readonly IProjectionWatermarkStore _watermarks;
    private readonly FlowableCacheOptions _options;

    public FlowableHistoryPollingFeed(
        IFlowableClient flowable,
        IProjectionWatermarkStore watermarks,
        IOptions<FlowableCacheOptions> options,
        ILogger<FlowableHistoryPollingFeed> logger)
        : base("flowable.history.poll", options.Value.HistoryPollInterval, logger)
    {
        _flowable = flowable;
        _watermarks = watermarks;
        _options = options.Value;
    }

    /// <summary>
    /// One tick, for tests. Same shape as <c>WorkflowCacheRetentionService</c>
    /// and <c>WorkflowTaskCompletionSweep</c> expose, so a test can drive a sweep
    /// without waiting on the timer loop.
    /// </summary>
    /// <returns>How many events this tick emitted — which is what makes the
    /// boundary behaviour observable, rather than only the page count.</returns>
    internal Task<int> RunTickOnceAsync(CancellationToken cancellationToken) =>
        SweepAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken) =>
        await SweepAsync(cancellationToken);

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var emitted = 0;
        var watermark = await _watermarks.GetAsync(FeedName, cancellationToken);
        var start = 0;
        var pageSize = Math.Max(1, _options.HistoryPageSize);
        DateTimeOffset? newWatermark = watermark;
        var reachedKnownHistory = false;

        // NEWEST FIRST, STOPPING AT WHAT WE ALREADY HAVE (#590).
        //
        // This used to page ASCENDING from the beginning with a `startedAfter`
        // the server ignores, so every tick re-read all of history -- 3162 events
        // and growing -- while the watermark advanced and the logs read as
        // incremental. The watermark was decorative.
        //
        // It is now the stop condition. Descending order IS honoured by Flowable,
        // so once a page yields an event older than the watermark, everything
        // beyond it is older still and already recorded.
        while (!cancellationToken.IsCancellationRequested && !reachedKnownHistory)
        {
            var page = await _flowable.GetHistoricActivityEventsAsync(start, pageSize, cancellationToken);
            if (page.Count == 0) break;

            foreach (var ev in page)
            {
                // STRICTLY older, not older-or-equal. Several events can share a
                // timestamp, and stopping at equality would drop the ones a
                // previous tick had not reached yet. Re-reading the boundary
                // second is harmless -- the projection is idempotent on event_id
                // -- and missing an event is not.
                if (watermark is { } mark && ev.StartTime is { } when && when < mark)
                {
                    reachedKnownHistory = true;
                    break;
                }

                await EmitAsync(
                    new ChangeEvent<FlowableHistoricActivityEvent>(
                        ChangeOp.Upsert,
                        // SourceId is informational for the framework; the
                        // projection derives the actual event_id (one per
                        // (activity, kind) pair).
                        $"{ev.ProcessInstanceId}/{ev.ActivityId}",
                        ev,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
                emitted++;

                if (ev.StartTime is { } st && (newWatermark is null || st > newWatermark))
                {
                    newWatermark = st;
                }
            }

            if (page.Count < pageSize) break;
            start += page.Count;
        }

        if (newWatermark is { } w && w != watermark)
        {
            await _watermarks.SetAsync(FeedName, w, cancellationToken);
        }

        return emitted;
    }
}
