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
            // #634. NOT NECESSARILY DELETED -- it may simply have finished.
            //
            // `GetProcessInstanceAsync` queries `service/runtime/process-instances/{id}`
            // and maps 404 to null, and Flowable returns 404 there for every
            // COMPLETED instance; the runtime table holds only live ones.
            // Measured against the engine: a finished instance's runtime GET is
            // 404 while its history row is intact.
            //
            // Reading that as deletion did real damage. Once a finished run's row
            // passed ReadThroughFreshness (30s) inside the 60s poll interval --
            // roughly half of every cycle -- the row was deleted, so
            // `ExistsAndAuthorizedAsync` returned false and every
            // RequirePermission(..., "processInstanceId") route 403'd for
            // non-super-admins, while the executions list lost the run until the
            // next poll re-inserted it. Measured on the dev database: 4,536
            // completed and 358 cancelled rows were in scope.
            //
            // So a terminal row is SERVED, not deleted. Deletion stays the poll's
            // job: it enumerates, so absence there is a fact about the engine
            // rather than an inference from one endpoint that was asked the wrong
            // question.
            // #658. ...and the poll does not delete either -- it only upserts --
            // so a served-forever terminal row was the other failure: an instance
            // deleted from the engine (delete-all, history cleanup) kept a cache
            // row that authorized every instance gate. History is the tie-break:
            // a finished run is still in history; a run that is gone is not.
            if (cached is not null && WorkflowExecutionStatuses.IsTerminal(cached.Status))
            {
                if (await _flowable.HistoricProcessInstanceExistsAsync(instanceId, cancellationToken))
                {
                    return cached;
                }
            }

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
            LastActivityAtUtc = DateTimeOffset.UtcNow,

            // #583. The projection's upsert writes EVERY column, so anything not
            // set here is written as null over whatever the poll had put there.
            //
            // `Name` the runtime instance does carry, so it is authoritative.
            // `WorkflowModelName` it does not -- FlowableProcessInstanceSummary
            // has no such field -- so it is carried forward from the cached row
            // rather than blanked. That is the one place these two write paths
            // differ, and it is named here rather than left to emerge as a name
            // that disappears whenever a detail view goes stale.
            WorkflowModelName = cached?.WorkflowModelName
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
