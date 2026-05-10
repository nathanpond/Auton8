using System.Text.Json;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable;
using Microsoft.EntityFrameworkCore;
using SiteSettingEntity = AutoNate.Web.Persistence.Scaffolded.SiteSetting;

namespace AutoNate.Web.Services.Notifications;

// One-time backfill: clears notifications whose backing entity (record or
// workflow execution) is no longer active. The ongoing event-driven cleanup
// in EfCoreRecordStore + WorkflowTaskNotificationListener handles future
// drift; this service exists for rows that were created before the cleanup
// hooks landed.
//
// Idempotency: a marker row in site_settings prevents re-running. The marker
// records the number of rows deleted by class so operators can audit. Bumping
// the version constant is the recommended way to re-run after a code change.
public sealed class OrphanedNotificationCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<OrphanedNotificationCleanupService> logger) : IHostedService
{
    // v2 adds the LinkPath → parent_entity_id backfill for legacy rows. Bump
    // again whenever the cleanup criteria expand.
    private const string MarkerKey = "notifications.orphan_cleanup_v2";

    private CancellationTokenSource? _cts;
    private Task? _runner;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Run on the background pool so we don't block app startup. Hold a
        // CTS so StopAsync can short-circuit a long-running Flowable scan
        // during shutdown.
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runner = Task.Run(() => SafeRunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null) await _cts.CancelAsync();
        if (_runner is not null)
        {
            try { await _runner.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task SafeRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown — no work lost; marker not written, will retry on next start.
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Orphaned-notification cleanup failed. Marker not written; will retry next start.");
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        await using (var probeDb = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var existing = await probeDb.SiteSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == MarkerKey, cancellationToken);
            if (existing is not null)
            {
                logger.LogDebug("Skipping orphan cleanup — marker {Marker} already present.", MarkerKey);
                return;
            }
        }

        var backfilled = await BackfillWorkflowTaskParentIdsAsync(dbContextFactory, cancellationToken);
        var recordOrphans = await DeleteOrphanedRecordNotificationsAsync(dbContextFactory, cancellationToken);
        var taskOrphans = await DeleteOrphanedWorkflowTaskNotificationsAsync(
            scope.ServiceProvider, dbContextFactory, cancellationToken);

        await WriteMarkerAsync(dbContextFactory, backfilled, recordOrphans, taskOrphans, cancellationToken);
        logger.LogInformation(
            "Orphaned-notification cleanup complete. Backfilled parent on {Backfilled} legacy rows. Deleted {RecordOrphans} record notifications and {TaskOrphans} workflow-task notifications.",
            backfilled, recordOrphans, taskOrphans);
    }

    private static async Task<int> BackfillWorkflowTaskParentIdsAsync(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        // Legacy rows (created before parent_entity_id existed) have the
        // process instance id embedded in link_path as "/executions/{id}".
        // Lift it into parent_entity_id so both the orphan scan below and the
        // event-driven DeleteByParentEntityAsync path can target them.
        const string sql = """
            UPDATE notifications
            SET parent_entity_kind = 'workflow_execution',
                parent_entity_id = substring(link_path from '^/executions/(.+)$')
            WHERE kind = 'workflow.task.assigned'
              AND parent_entity_id IS NULL
              AND link_path ~ '^/executions/.+$';
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static async Task<int> DeleteOrphanedRecordNotificationsAsync(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        // A record-assignment notification is orphaned if no records row exists
        // for it where (a) the record is not archived AND (b) the recipient is
        // still listed in assignee_ids. The cast on related_entity_id (TEXT)
        // guards against malformed ids — bad rows fall through and are kept.
        const string sql = """
            DELETE FROM notifications
            WHERE kind = 'record.assigned'
              AND related_entity_kind = 'record'
              AND related_entity_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM records r
                  WHERE r.id::text = notifications.related_entity_id
                    AND r.is_archived = FALSE
                    AND notifications.user_id = ANY (r.assignee_ids)
              );
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task<int> DeleteOrphanedWorkflowTaskNotificationsAsync(
        IServiceProvider services,
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Only consider rows that carry a parent reference — rows created
        // before the parent column existed don't have an execution to anchor
        // against, and a per-task Flowable round-trip for each one is more
        // than this one-shot is worth. The event-driven cleanup will catch
        // them as their host execution closes.
        var rows = await dbContext.Notifications.AsNoTracking()
            .Where(n => n.Kind == NotificationKinds.WorkflowTaskAssigned
                        && n.ParentEntityKind == NotificationEntityKinds.WorkflowExecution
                        && n.ParentEntityId != null
                        && n.RelatedEntityKind == NotificationEntityKinds.WorkflowTask
                        && n.RelatedEntityId != null)
            .Select(n => new { n.Id, ProcessInstanceId = n.ParentEntityId!, TaskId = n.RelatedEntityId! })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        var flowable = services.GetRequiredService<IFlowableClient>();
        var orphanIds = new List<Guid>();
        var liveTasksByProcess = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var deadProcesses = new HashSet<string>(StringComparer.Ordinal);

        foreach (var processGroup in rows.GroupBy(r => r.ProcessInstanceId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processInstanceId = processGroup.Key;

            HashSet<string>? liveTaskIds = null;
            try
            {
                var instance = await flowable.GetProcessInstanceAsync(processInstanceId, cancellationToken);
                if (instance is null)
                {
                    deadProcesses.Add(processInstanceId);
                }
                else
                {
                    var liveTasks = await flowable.GetTasksByProcessInstanceAsync(processInstanceId, cancellationToken);
                    liveTaskIds = new HashSet<string>(
                        liveTasks.Select(t => t.Id).Where(id => !string.IsNullOrEmpty(id)),
                        StringComparer.Ordinal);
                    liveTasksByProcess[processInstanceId] = liveTaskIds;
                }
            }
            catch (Exception ex)
            {
                // Flowable might be temporarily unavailable. Skip this process
                // group rather than misclassify rows as orphans.
                logger.LogWarning(ex,
                    "Skipping orphan check for process instance {ProcessInstanceId} — Flowable lookup failed.",
                    processInstanceId);
                continue;
            }

            foreach (var row in processGroup)
            {
                if (deadProcesses.Contains(processInstanceId))
                {
                    orphanIds.Add(row.Id);
                }
                else if (liveTaskIds is not null && !liveTaskIds.Contains(row.TaskId))
                {
                    orphanIds.Add(row.Id);
                }
            }
        }

        if (orphanIds.Count == 0)
        {
            return 0;
        }

        return await dbContext.Notifications
            .Where(n => orphanIds.Contains(n.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task WriteMarkerAsync(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        int parentBackfilled,
        int recordOrphans,
        int taskOrphans,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var summary = new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            workflowTaskParentBackfilled = parentBackfilled,
            recordOrphansDeleted = recordOrphans,
            workflowTaskOrphansDeleted = taskOrphans
        };
        var marker = new SiteSettingEntity
        {
            Key = MarkerKey,
            ValueJson = JsonSerializer.Serialize(summary),
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = Guid.Empty
        };
        dbContext.SiteSettings.Add(marker);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race with another instance of the service (parallel startup) —
            // safe to swallow: marker is now in place.
        }
    }
}
