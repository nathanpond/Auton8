using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

/// <summary>
/// Teaches <c>workflow_task_cache</c> that a task finished (#586).
/// </summary>
/// <remarks>
/// <para>
/// Nothing used to. <c>FlowableTaskProjection.MapRow</c> writes
/// <c>CompletedTime = null</c> and <c>Status = "active"</c> unconditionally, and
/// no producer ever emits a <c>ChangeOp.Delete</c> for a task — the runtime poll
/// lists only open tasks, so a completed one simply stops appearing and its row
/// is orphaned in the <c>active</c> state until retention deletes it by process
/// age, 2555 days later.
/// </para>
/// <para>
/// The visible consequence was in <c>FlowsQueryEntity</c>, which filters
/// <c>Status == "active" &amp;&amp; CompletedTime == null</c> and takes the oldest
/// match as the current step. That filter could not exclude a single row, so
/// <c>CURRENTSTEP()</c> reported an instance's first task forever.
/// </para>
///
/// <para><b>Completion is read as a positive fact, never inferred from absence.</b>
/// The cheaper design — diff a runtime sweep against the cache and mark what is
/// missing — is unsound here: the polling feed emits into a channel the
/// projection drains asynchronously, so when a sweep finishes its own upserts may
/// not have been applied yet, and rows would be marked complete for missing a
/// sweep whose results had not landed. Reading Flowable's history means a partial
/// or failed sweep can only do less, never something wrong.</para>
///
/// <para><b>Bounded by the sort, because it cannot be bounded by time.</b> Measured
/// against a live engine: <c>finishedAfter</c> is ignored on this endpoint — a
/// value of 2030 returns the same rows as no filter — and an invented parameter
/// returns 200 with everything, so a watermark pushed to the server would have
/// looked correct while reprocessing all of history. <c>sort=endTime&amp;order=desc</c>
/// IS honoured, so the sweep walks newest-first and stops as soon as a page marks
/// nothing new. Everything older is older still, so steady state is one page per
/// tick; only the first sweep after deployment walks further.</para>
/// </remarks>
public sealed class WorkflowTaskCompletionSweep : BackgroundService
{
    private readonly IFlowableClient _flowable;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly FlowableCacheOptions _options;
    private readonly ILogger<WorkflowTaskCompletionSweep> _logger;

    public WorkflowTaskCompletionSweep(
        IFlowableClient flowable,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<FlowableCacheOptions> options,
        ILogger<WorkflowTaskCompletionSweep> logger)
    {
        _flowable = flowable;
        _dbFactory = dbFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed sweep leaves rows unmarked, which is the same state as
                // before the sweep ran. It cannot leave them WRONGLY marked,
                // because every mark comes from a completion Flowable reported.
                _logger.LogError(ex, "Task completion sweep failed; retrying after interval.");
            }

            try { await Task.Delay(_options.TaskPollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// One sweep. Public so tests can drive it without waiting for the loop —
    /// the same shape <c>WorkflowCacheRetentionService</c> uses.
    /// </summary>
    /// <returns>How many cache rows this sweep moved from active to completed.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var pageSize = Math.Max(1, _options.TaskPageSize);
        var start = 0;
        var marked = 0;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await _flowable.GetFinishedTasksAsync(start, pageSize, cancellationToken);
            if (page.Count == 0) break;

            var markedInPage = 0;
            foreach (var task in page)
            {
                markedInPage += await MarkCompletedAsync(db, task, cancellationToken);
            }

            marked += markedInPage;

            // THE STOP CONDITION, and the reason the sort matters. In endTime
            // DESC order, a page that marked nothing means every task in it was
            // already known complete -- and everything beyond it finished even
            // earlier, so it is known too. Continuing would re-walk history.
            //
            // Note it is "marked nothing", not "the page was short": a short page
            // is the end of history and also stops, below.
            if (markedInPage == 0) break;
            if (page.Count < pageSize) break;

            start += page.Count;
        }

        if (marked > 0)
        {
            _logger.LogInformation(
                "Task completion sweep marked {Marked} cache row(s) completed.", marked);
        }

        return marked;
    }

    // Targeted UPDATE rather than an upsert. The historic payload is a different
    // shape from the runtime one, and building a full row from it would blank
    // columns the runtime projection fills -- the failure #583 found in
    // FlowableReadThrough, where an upsert assembled from a partial source
    // erased a field another writer owned.
    //
    // The WHERE clause is what makes the sweep idempotent and makes the return
    // value mean "newly marked": a row already completed matches nothing.
    private static async Task<int> MarkCompletedAsync(
        AutoNateDbContext db, Models.FlowableFinishedTask task, CancellationToken cancellationToken)
    {
        var endedAt = task.EndedAtUtc?.UtcDateTime ?? DateTime.UtcNow;

        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE workflow_task_cache
               SET status = 'completed',
                   completed_time = {endedAt}
             WHERE flowable_task_id = {task.Id}
               AND completed_time IS NULL
            """, cancellationToken);
    }
}
