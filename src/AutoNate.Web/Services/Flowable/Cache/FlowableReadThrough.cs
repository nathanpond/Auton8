using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

public sealed class FlowableReadThrough : IFlowableReadThrough
{
    private readonly IFlowableClient _flowable;
    private readonly FlowableExecutionProjection _projection;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly FlowableCacheOptions _options;

    public FlowableReadThrough(
        IFlowableClient flowable,
        FlowableExecutionProjection projection,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<FlowableCacheOptions> options)
    {
        _flowable = flowable;
        _projection = projection;
        _dbFactory = dbFactory;
        _options = options.Value;
    }

    public async Task<WorkflowExecutionCache?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var cached = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FlowableInstanceId == instanceId, cancellationToken);

        var freshThreshold = DateTime.UtcNow - _options.ReadThroughFreshness;
        if (cached is not null && cached.LastSyncAtUtc >= freshThreshold)
        {
            return cached;
        }

        // Cache miss or stale — hit Flowable. Even for a stale read we still
        // return the cache row if the live fetch fails, so a Flowable hiccup
        // doesn't degrade detail views below their previous freshness.
        FlowableProcessInstanceSummary? live;
        try
        {
            live = await _flowable.GetProcessInstanceAsync(instanceId, cancellationToken);
        }
        catch
        {
            return cached;
        }

        if (live is null)
        {
            // Instance has been deleted in Flowable. Clear the cache row so
            // future reads don't keep serving a tombstone.
            if (cached is not null)
            {
                await _projection.ApplyAsync(new[]
                {
                    new ChangeEvent<WorkflowExecutionSummary>(
                        ChangeOp.Delete, instanceId, null, DateTimeOffset.UtcNow)
                }, db, cancellationToken);
            }
            return null;
        }

        // Translate the runtime-instance summary to the projection's source
        // shape and write through. The fields we don't have here (status,
        // current activity name) get filled by the next polling tick — for a
        // detail view, the row's existence + identity is the priority.
        var summary = new WorkflowExecutionSummary
        {
            Id = live.Id,
            Name = live.Name,
            ProcessDefinitionId = live.ProcessDefinitionId,
            Status = live.Suspended ? "Suspended" : "Running",
            StartUserId = live.StartUserId,
            CurrentStep = live.ActivityId,
            StartedAtUtc = cached?.StartTime is { } st ? new DateTimeOffset(DateTime.SpecifyKind(st, DateTimeKind.Utc)) : null,
            LastActivityAtUtc = DateTimeOffset.UtcNow
        };

        await _projection.ApplyAsync(new[]
        {
            new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, instanceId, summary, DateTimeOffset.UtcNow)
        }, db, cancellationToken);

        return await db.WorkflowExecutionCache.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FlowableInstanceId == instanceId, cancellationToken);
    }
}
