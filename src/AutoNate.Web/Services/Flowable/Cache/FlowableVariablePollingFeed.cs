using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Iterates active instances in workflow_execution_cache (the executions feed
// already populates this) and fetches each one's variables. Bounded fan-out:
// we limit per-tick instance scans to MaxInstancesPerTick so a Flowable with
// thousands of active runs doesn't burst-overload the REST API. The
// projection's snapshot semantics let us skip per-tick dedup logic.
public sealed class FlowableVariablePollingFeed : PeriodicPollingFeed<FlowableInstanceVariables>
{
    private readonly IFlowableClient _flowable;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly FlowableCacheOptions _options;
    private readonly ILogger<FlowableVariablePollingFeed> _logger;

    public FlowableVariablePollingFeed(
        IFlowableClient flowable,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<FlowableCacheOptions> options,
        ILogger<FlowableVariablePollingFeed> logger)
        : base("flowable.var.poll", options.Value.VariablePollInterval, logger)
    {
        _flowable = flowable;
        _dbFactory = dbFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var activeIds = await db.WorkflowExecutionCache
            .AsNoTracking()
            .Where(c => c.Status == "active")
            .OrderByDescending(c => c.StartTime)
            .Take(_options.VariableInstancesPerTick)
            .Select(c => c.FlowableInstanceId)
            .ToListAsync(cancellationToken);

        foreach (var instanceId in activeIds)
        {
            try
            {
                var variables = await _flowable.GetProcessInstanceVariablesAsync(instanceId, cancellationToken);
                await EmitAsync(
                    new ChangeEvent<FlowableInstanceVariables>(
                        ChangeOp.Upsert, instanceId,
                        new FlowableInstanceVariables(instanceId, variables),
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One instance failing (deleted between SELECT and fetch,
                // Flowable transient error, etc.) shouldn't stall the tick.
                // The next tick will pick this instance up again if it's
                // still active.
                _logger.LogWarning(ex,
                    "Variable fetch failed for instance {InstanceId}; skipping until next tick.",
                    instanceId);
            }
        }
    }
}
