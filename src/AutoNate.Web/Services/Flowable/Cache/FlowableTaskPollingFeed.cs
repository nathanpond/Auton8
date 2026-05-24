using AutoNate.Web.Models;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Pages through Flowable's runtime task list every tick and emits upserts.
// Like the execution feed, idempotency at the projection layer makes
// repeated emission safe.
//
// Doesn't currently emit deletes for completed tasks — completion arrives
// either via the in-app COMPLETE endpoint (which can write directly through
// FlowableReadThrough) or by absence from the next poll (the reconciliation
// pass in Phase 2 will mark/sweep those).
public sealed class FlowableTaskPollingFeed : PeriodicPollingFeed<FlowableTaskSummary>
{
    private readonly IFlowableClient _flowable;
    private readonly FlowableCacheOptions _options;

    public FlowableTaskPollingFeed(
        IFlowableClient flowable,
        IOptions<FlowableCacheOptions> options,
        ILogger<FlowableTaskPollingFeed> logger)
        : base("flowable.task.poll", options.Value.TaskPollInterval, logger)
    {
        _flowable = flowable;
        _options = options.Value;
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        var start = 0;
        var pageSize = Math.Max(1, _options.TaskPageSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await _flowable.GetRuntimeTasksAsync(start, pageSize, cancellationToken);
            if (page.Count == 0) return;

            foreach (var task in page)
            {
                if (string.IsNullOrWhiteSpace(task.Id)) continue;
                await EmitAsync(
                    new ChangeEvent<FlowableTaskSummary>(
                        ChangeOp.Upsert, task.Id, task, DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            if (page.Count < pageSize) return;
            start += page.Count;
        }
    }
}
