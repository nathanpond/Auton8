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

    public FlowableExecutionPollingFeed(
        IFlowableClient flowable,
        IOptions<FlowableCacheOptions> options,
        ILogger<FlowableExecutionPollingFeed> logger)
        : base("flowable.exec.poll", options.Value.ExecutionPollInterval, logger)
    {
        _flowable = flowable;
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        var instances = await _flowable.GetWorkflowExecutionsAsync(cancellationToken);
        foreach (var instance in instances)
        {
            if (string.IsNullOrWhiteSpace(instance.Id)) continue;
            await EmitAsync(
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, instance.Id, instance, DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }
}
