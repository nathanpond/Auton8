using AutoNate.Web.Models;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Pages the global Flowable historic-activity-instances endpoint sinceUtc =
// watermark. Each page advances the watermark to the latest StartTime seen
// so a restart resumes from where we left off rather than replaying the
// entire history. The append-only projection makes occasional overlap
// (boundary clock skew) harmless.
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

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        var watermark = await _watermarks.GetAsync(FeedName, cancellationToken);
        var start = 0;
        var pageSize = Math.Max(1, _options.HistoryPageSize);
        var newWatermark = watermark;

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await _flowable.GetHistoricActivityEventsAsync(start, pageSize, watermark, cancellationToken);
            if (page.Count == 0) break;

            foreach (var ev in page)
            {
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
    }
}
