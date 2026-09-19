using AutoNate.Web.Models;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Pulls the most-recent N process instances on a timer and emits them as
// upsert ChangeEvents. The projection is idempotent on flowable_instance_id,
// so re-emitting the same instance repeatedly is just a cheap no-op update
// of the `last_sync_at` column.
//
// For an unbounded backfill of older instances, use BackfillRunner +
// FlowableExecutionBackfillSource (defined separately).
public sealed class FlowableExecutionPollingFeed : PeriodicPollingFeed<WorkflowExecutionSummary>
{
    private readonly IFlowableClient _flowable;
    private readonly IProjectionWatermarkStore _watermarks;
    private readonly FlowableCacheOptions _options;

    public FlowableExecutionPollingFeed(
        IFlowableClient flowable,
        IProjectionWatermarkStore watermarks,
        IOptions<FlowableCacheOptions> options,
        ILogger<FlowableExecutionPollingFeed> logger)
        : base("flowable.exec.poll", options.Value.ExecutionPollInterval, logger)
    {
        _flowable = flowable;
        _watermarks = watermarks;
        _options = options.Value;
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        // Bounded per tick (#588). See FlowableCacheOptions.ExecutionPollMaxPages
        // for why the ceiling lives here and not in the client.
        var instances = await _flowable.GetWorkflowExecutionsAsync(
            _options.ExecutionPollMaxPages, cancellationToken);
        foreach (var instance in instances)
        {
            if (string.IsNullOrWhiteSpace(instance.Id)) continue;
            await EmitAsync(
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, instance.Id, instance, DateTimeOffset.UtcNow),
                cancellationToken);
        }

        // HEARTBEAT (#594). Written only after the whole tick succeeded -- a tick
        // that threw never reaches here, which is what lets a reader tell "the
        // feed is not updating" from "nothing has changed lately".
        //
        // THE SEMANTIC OVERLOAD IS DELIBERATE AND WORTH NAMING. For the history
        // feed a watermark means "how far I have read". This fetch has no time
        // filter to resume from -- GetWorkflowExecutionsAsync pages from the start
        // every tick -- so here it means "when the last sweep completed". It rides
        // projection_watermarks rather than an in-process flag so every replica
        // reads the same answer, and rather than a new table because one row per
        // feed already exists for exactly this kind of bookkeeping.
        await _watermarks.SetAsync(FeedName, DateTimeOffset.UtcNow, cancellationToken);
    }
}
