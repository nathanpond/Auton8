using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Flowable.Cache;

/// <summary>
/// Makes a task completion visible to the caller that caused it (#604).
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/executions/{id}/tasks</c> serves from <c>workflow_task_cache</c>,
/// and nothing on the completion path wrote to it. The row kept
/// <c>status = 'active'</c> and <c>completed_time = null</c>, and the successor
/// the engine created was not projected, until whichever background pass ran
/// next — <c>TaskPollInterval</c> is a minute, and <c>ReadThroughFreshness</c>
/// is thirty seconds, so even a re-read inside the window returned the row that
/// had just been finished.
/// </para>
/// <para>
/// <b>Measured, not reasoned.</b> Driven straight at Flowable the engine
/// advances immediately: complete task 1 over its own REST and task 2 is there
/// on the next call. It was Auton8's projection that had not heard. The visible
/// cost was 24 of 493 full-local E2E specs — every one of them a spec that
/// completes a task and waits for what comes next — and a person completing a
/// task in the UI seeing it listed as current for up to a minute.
/// </para>
/// <para>
/// <b>Completion stays a positive fact</b>, which is the rule
/// <see cref="WorkflowTaskCompletionSweep"/> was built on: the caller completed
/// this task and the engine returned success, so the row is marked from that
/// fact rather than inferred from its absence in a sweep. The two paths agree on
/// the shape they write, and the sweep's <c>completed_time IS NULL</c> guard
/// means whichever arrives second changes nothing.
/// </para>
/// <para>
/// <b>Failures are logged and swallowed</b>, the same trade
/// <c>CoalesceTasksForNewInstancesAsync</c> makes: the engine has already
/// accepted the completion, so throwing here would report failure for work that
/// succeeded. The minute-poll remains the safety net, which is what makes this
/// only ever able to make the cache fresher and never wrong.
/// </para>
/// </remarks>
public sealed class WorkflowTaskCacheRefresher(
    IFlowableClient flowable,
    FlowableTaskProjection taskProjection,
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<WorkflowTaskCacheRefresher> logger)
{
    private readonly IFlowableClient _flowable = flowable;
    private readonly FlowableTaskProjection _taskProjection = taskProjection;
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<WorkflowTaskCacheRefresher> _logger = logger;

    /// <summary>
    /// The instance's open tasks, read live when the cache is past its freshness
    /// window (#604).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Completion was only the trigger I happened to find first. <b>Measured:</b>
    /// fixing the completion path took the two failing execution suites from 7
    /// failures to 5, and the two that remained were a timer handler firing
    /// (<c>"Tasks were: Ongoing work"</c>) and an ad-hoc activity being started —
    /// neither of which completes anything. <b>Any task the engine creates on its
    /// own is invisible until the poll</b>, and the poll is a minute while the
    /// things watching for it give up in thirty seconds.
    /// </para>
    /// <para>
    /// So this is a detail view and it reads through on every call, the way the
    /// jobs endpoints in <c>ExecutionEndpoints</c> already do and for the reason
    /// stated there. The LIST endpoint stays cached — the distinction is between
    /// "how are my hundred flows doing" and "what is this one doing right now",
    /// and only the second is watched for change.
    /// </para>
    /// <para>
    /// <b>Nothing is inferred from absence.</b> The live answer is returned and
    /// written through, but a cached row the engine no longer lists is left
    /// alone: marking it complete from its absence would invent a
    /// <c>completed_time</c> nobody measured, which is the inference #586 refused
    /// for the sweep. The sweep and the completion path own that transition.
    /// </para>
    /// <para>
    /// A failed fetch returns <c>null</c> so the caller falls back to the cache,
    /// the same trade <c>GetInstanceAsync</c> makes: an engine hiccup degrades a
    /// detail view to its previous freshness rather than emptying it.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<FlowableTaskSummary>?> ReadThroughOpenTasksAsync(
        string instanceId,
        AutoNateDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;

        // NO FRESHNESS WINDOW, and that is the correction rather than the design.
        //
        // The first version gated this on the newest `last_sync_at` among the
        // instance's rows, the way `GetInstanceAsync` gates on its own. That is
        // incoherent here: THIS METHOD WRITES THAT COLUMN. One read-through
        // marked the rows fresh for the next thirty seconds, so a caller polling
        // every second got cached answers for thirty of them -- measured, as the
        // timer-handler spec still failing at its 30s budget after the rest of
        // the fix was in.
        //
        // A window whose clock its own reader resets is not a window. The jobs
        // endpoints in this file already settled the same question the same way,
        // and said why: the reader who opens a single execution is precisely the
        // one who cannot tolerate a staleness question.
        IReadOnlyList<FlowableTaskSummary> live;
        try
        {
            live = await _flowable.GetTasksByProcessInstanceAsync(instanceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Live task read failed for instance {InstanceId}; serving the cached rows.",
                instanceId);
            return null;
        }

        var observedAt = DateTimeOffset.UtcNow;
        var changes = live
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .Select(t => new ChangeEvent<FlowableTaskSummary>(
                ChangeOp.Upsert, t.Id, t, observedAt))
            .ToList();

        if (changes.Count > 0)
        {
            await _taskProjection.ApplyAsync(changes, db, cancellationToken);
        }

        return live;
    }

    /// <summary>
    /// Records that <paramref name="taskId"/> finished and re-projects its
    /// instance's open tasks, so the next read reflects this write.
    /// </summary>
    /// <param name="knownInstanceId">
    /// The instance, when the caller's route already names it. The task-scoped
    /// route does not, so it falls back to the cached row -- which is the only
    /// source once the task is gone from the engine's runtime, and is absent for
    /// a task completed before any poll ever saw it.
    /// </param>
    public async Task AfterTaskCompletedAsync(
        string taskId,
        CancellationToken cancellationToken = default,
        string? knownInstanceId = null)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return;

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // The instance comes from the cached row rather than from a
            // parameter: `/api/tasks/{taskId}/complete` is addressed by task
            // alone and never learns which instance it belonged to, and asking
            // the engine is pointless -- the task is gone from the runtime by
            // the time we are here.
            var instanceId = !string.IsNullOrWhiteSpace(knownInstanceId)
                ? knownInstanceId
                : await db.WorkflowTaskCache.AsNoTracking()
                    .Where(t => t.FlowableTaskId == taskId)
                    .Select(t => t.FlowableInstanceId)
                    .FirstOrDefaultAsync(cancellationToken);

            // Same shape as the sweep writes, including its `completed_time IS
            // NULL` guard, so whichever of the two arrives second is a no-op
            // rather than a second opinion about when this finished.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflow_task_cache
                   SET status = 'completed',
                       completed_time = {DateTime.UtcNow}
                 WHERE flowable_task_id = {taskId}
                   AND completed_time IS NULL
                """, cancellationToken);

            // A task the cache had never seen -- completed inside the same
            // minute it was created, before any poll -- leaves nothing to
            // re-project from, and there is no instance to ask about.
            if (string.IsNullOrWhiteSpace(instanceId)) return;

            var open = await _flowable.GetTasksByProcessInstanceAsync(instanceId, cancellationToken);

            var observedAt = DateTimeOffset.UtcNow;
            var changes = open
                .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                .Select(t => new ChangeEvent<FlowableTaskSummary>(
                    ChangeOp.Upsert, t.Id, t, observedAt))
                .ToList();

            if (changes.Count > 0)
            {
                await _taskProjection.ApplyAsync(changes, db, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not refresh workflow_task_cache after completing {TaskId}; the task poll "
                + "will catch up on its next tick.",
                taskId);
        }
    }
}
