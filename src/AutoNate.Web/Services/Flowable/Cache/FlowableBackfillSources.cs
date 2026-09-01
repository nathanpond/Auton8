using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Backfill sources for the Flowable-backed projections (#112).
//
// BackfillRunner resolves IProjectionBackfillSource<TSource> from DI and
// throws when none is registered, which AdminProjectionsEndpoints maps to a
// 400. No implementation existed, so the Rebuild button on the Projections
// admin page returned "No IProjectionBackfillSource<…> registered" for every
// projection, and the recovery path documented in
// docs/projection-framework/operations.md did not work.
//
// Each source re-emits from the same Flowable calls its polling feed uses,
// minus the feed's per-tick bounds — the point of a backfill is to ignore the
// windowing that keeps a tick cheap. Where Flowable's own API bounds what can
// be enumerated, that limit is called out on the class rather than hidden:
// the runner's contract is "re-emit everything reachable", and being explicit
// about what is reachable is the difference between a backfill an operator
// can trust and one that silently under-fills.

// Every process instance the runtime will hand back, emitted as upserts. The
// projection is idempotent on flowable_instance_id, so a rebuild that overlaps
// what the poll feed already wrote is a no-op update rather than a conflict.
//
// Bound: GetWorkflowExecutionsAsync returns the instances Flowable's query
// endpoint yields for the configured size, not the full historic archive.
// This is the same set the live feed sees, so a rebuild restores the cache to
// what steady-state polling would have produced — it does not resurrect
// instances aged out of Flowable itself.
public sealed class FlowableExecutionBackfillSource(IFlowableClient flowable)
    : IProjectionBackfillSource<WorkflowExecutionSummary>
{
    public string ProjectionName => "flowable.workflow_execution_cache";

    public async IAsyncEnumerable<ChangeEvent<WorkflowExecutionSummary>> EnumerateAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var instances = await flowable.GetWorkflowExecutionsAsync(cancellationToken);
        foreach (var instance in instances)
        {
            if (string.IsNullOrWhiteSpace(instance.Id)) continue;
            yield return new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, instance.Id, instance, DateTimeOffset.UtcNow);
        }
    }
}

// Runtime tasks, paged the way the poll feed pages them but without stopping
// at a tick boundary.
public sealed class FlowableTaskBackfillSource(
    IFlowableClient flowable,
    IOptions<FlowableCacheOptions> options)
    : IProjectionBackfillSource<FlowableTaskSummary>
{
    public string ProjectionName => "flowable.workflow_task_cache";

    public async IAsyncEnumerable<ChangeEvent<FlowableTaskSummary>> EnumerateAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var start = 0;
        var pageSize = Math.Max(1, options.Value.TaskPageSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await flowable.GetRuntimeTasksAsync(start, pageSize, cancellationToken);
            if (page.Count == 0) yield break;

            foreach (var task in page)
            {
                if (string.IsNullOrWhiteSpace(task.Id)) continue;
                yield return new ChangeEvent<FlowableTaskSummary>(
                    ChangeOp.Upsert, task.Id, task, DateTimeOffset.UtcNow);
            }

            if (page.Count < pageSize) yield break;
            start += page.Count;
        }
    }
}

// Variables for every instance in the execution cache — not just the active
// ones the poll feed samples per tick, and with no per-tick instance cap.
// Reads instance ids from the cache rather than Flowable because that is the
// set the variable projection is keyed against.
public sealed class FlowableVariableBackfillSource(
    IFlowableClient flowable,
    IDbContextFactory<AutoNateDbContext> dbFactory,
    ILogger<FlowableVariableBackfillSource> logger)
    : IProjectionBackfillSource<FlowableInstanceVariables>
{
    public string ProjectionName => "flowable.workflow_variable_cache";

    public async IAsyncEnumerable<ChangeEvent<FlowableInstanceVariables>> EnumerateAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var instanceIds = await db.WorkflowExecutionCache
            .AsNoTracking()
            .OrderBy(c => c.FlowableInstanceId)
            .Select(c => c.FlowableInstanceId)
            .ToListAsync(cancellationToken);

        foreach (var instanceId in instanceIds)
        {
            IReadOnlyDictionary<string, System.Text.Json.JsonElement> variables;
            try
            {
                variables = await flowable.GetProcessInstanceVariablesAsync(instanceId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                // One instance failing must not abort a long rebuild — the
                // same reasoning the poll feed uses, and more important here
                // because a backfill may run for thousands of instances.
                logger.LogWarning(ex,
                    "Variable backfill skipped instance {InstanceId}.", instanceId);
                continue;
            }

            yield return new ChangeEvent<FlowableInstanceVariables>(
                ChangeOp.Upsert, instanceId,
                new FlowableInstanceVariables(instanceId, variables),
                DateTimeOffset.UtcNow);
        }
    }
}

// Historic activity events from the beginning of Flowable's history, ignoring
// the feed's watermark — a rebuild exists precisely to re-read what the
// watermark says has already been seen.
public sealed class FlowableHistoryBackfillSource(
    IFlowableClient flowable,
    IOptions<FlowableCacheOptions> options)
    : IProjectionBackfillSource<FlowableHistoricActivityEvent>
{
    public string ProjectionName => "flowable.workflow_event_log_cache";

    public async IAsyncEnumerable<ChangeEvent<FlowableHistoricActivityEvent>> EnumerateAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var start = 0;
        var pageSize = Math.Max(1, options.Value.HistoryPageSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            // null watermark = from the beginning.
            var page = await flowable.GetHistoricActivityEventsAsync(
                start, pageSize, null, cancellationToken);
            if (page.Count == 0) yield break;

            foreach (var ev in page)
            {
                yield return new ChangeEvent<FlowableHistoricActivityEvent>(
                    ChangeOp.Upsert,
                    $"{ev.ProcessInstanceId}/{ev.ActivityId}",
                    ev,
                    DateTimeOffset.UtcNow);
            }

            if (page.Count < pageSize) yield break;
            start += page.Count;
        }
    }
}
