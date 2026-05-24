using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Translates WorkflowExecutionSummary → workflow_execution_cache rows.
// Owns the auth-tag extraction so the selector compiler can predicate on
// {startedby, processkey, definitionkey, status} without a separate
// lookup table.
//
// Idempotent: ON CONFLICT DO UPDATE on flowable_instance_id. Multiple feeds
// (poll, sweeper, NATS bridge) can converge on the same row safely.
public sealed class FlowableExecutionProjection : IProjection<WorkflowExecutionSummary>
{
    private readonly FlowableCacheOptions _options;
    private readonly IFlowableClient _flowable;
    private readonly FlowableTaskProjection _taskProjection;
    private readonly ILogger<FlowableExecutionProjection> _logger;

    public FlowableExecutionProjection(
        IOptions<FlowableCacheOptions> options,
        IFlowableClient flowable,
        FlowableTaskProjection taskProjection,
        ILogger<FlowableExecutionProjection> logger)
    {
        _options = options.Value;
        _flowable = flowable;
        _taskProjection = taskProjection;
        _logger = logger;
    }

    public string Name => "flowable.workflow_execution_cache";

    public int Version => _options.CurrentProjectionVersion;

    public Type SourceType => typeof(WorkflowExecutionSummary);

    public async Task ApplyAsync(
        IReadOnlyList<ChangeEvent<WorkflowExecutionSummary>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        // Collapse same-id repeats within a single batch to "latest wins" to
        // keep the multi-row upsert small. Order is preserved by ObservedAt.
        var latest = new Dictionary<string, ChangeEvent<WorkflowExecutionSummary>>(StringComparer.Ordinal);
        var deletes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in batch)
        {
            if (change.Op == ChangeOp.Delete)
            {
                latest.Remove(change.SourceId);
                deletes.Add(change.SourceId);
            }
            else
            {
                deletes.Remove(change.SourceId);
                latest[change.SourceId] = change;
            }
        }

        // Snapshot which IDs in this batch are *new to the cache* before the
        // upsert runs. We need this to drive the coalesce — fetching tasks
        // for an instance the cache has already seen is wasteful (the task
        // feed handles it) and an active flow's task set churns too quickly
        // to be worth re-fetching on every execution upsert.
        var firstSeenIds = _options.CoalesceTasksOnNewInstance && latest.Count > 0
            ? await IdentifyFirstSeenAsync(latest.Keys.ToList(), db, cancellationToken)
            : Array.Empty<string>();

        var now = DateTime.UtcNow;
        foreach (var change in latest.Values)
        {
            var src = change.Source!;
            var row = MapRow(src, now);
            // Per-row upsert via raw SQL keeps things simple and avoids EF's
            // change-tracker overhead on hot paths. Postgres will still
            // batch the round trips inside a single transaction.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workflow_execution_cache (
                    flowable_instance_id, process_definition_key, process_definition_id,
                    process_definition_version, business_key, tenant_id, status,
                    start_time, end_time, duration_ms, started_by,
                    current_activity_id, current_activity_name, record_id,
                    auth_tags, projection_version, last_sync_at)
                VALUES (
                    {row.FlowableInstanceId}, {row.ProcessDefinitionKey}, {row.ProcessDefinitionId},
                    {row.ProcessDefinitionVersion}, {row.BusinessKey}, {row.TenantId}, {row.Status},
                    {row.StartTime}, {row.EndTime}, {row.DurationMs}, {row.StartedBy},
                    {row.CurrentActivityId}, {row.CurrentActivityName}, {row.RecordId},
                    {row.AuthTagsJson}::jsonb, {row.ProjectionVersion}, {row.LastSyncAtUtc})
                ON CONFLICT (flowable_instance_id) DO UPDATE SET
                    process_definition_key     = EXCLUDED.process_definition_key,
                    process_definition_id      = EXCLUDED.process_definition_id,
                    process_definition_version = EXCLUDED.process_definition_version,
                    business_key               = EXCLUDED.business_key,
                    tenant_id                  = EXCLUDED.tenant_id,
                    status                     = EXCLUDED.status,
                    start_time                 = EXCLUDED.start_time,
                    end_time                   = EXCLUDED.end_time,
                    duration_ms                = EXCLUDED.duration_ms,
                    started_by                 = EXCLUDED.started_by,
                    current_activity_id        = EXCLUDED.current_activity_id,
                    current_activity_name      = EXCLUDED.current_activity_name,
                    record_id                  = EXCLUDED.record_id,
                    auth_tags                  = EXCLUDED.auth_tags,
                    projection_version         = EXCLUDED.projection_version,
                    last_sync_at               = EXCLUDED.last_sync_at
                """, cancellationToken);
        }

        foreach (var id in deletes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workflow_execution_cache WHERE flowable_instance_id = {id}",
                cancellationToken);
        }

        // Coalesce: now that the execution rows are persisted, pull tasks
        // for any instance the cache hadn't seen before this batch and run
        // them through the task projection in the same DbContext. Without
        // this, CURRENTSTEP() on a freshly-cached flow returns null until
        // the next FlowableTaskPollingFeed tick (~60s gap).
        if (firstSeenIds.Count > 0)
        {
            await CoalesceTasksForNewInstancesAsync(firstSeenIds, db, cancellationToken);
        }
    }

    // Returns the subset of `candidateIds` that don't already exist in
    // workflow_execution_cache. Used to drive the coalesce — anything not
    // returned here is either an existing instance (task feed owns it) or
    // not in the batch at all.
    private static async Task<IReadOnlyList<string>> IdentifyFirstSeenAsync(
        List<string> candidateIds,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        var existing = await db.WorkflowExecutionCache.AsNoTracking()
            .Where(c => candidateIds.Contains(c.FlowableInstanceId))
            .Select(c => c.FlowableInstanceId)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0) return candidateIds;
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);
        return candidateIds.Where(id => !existingSet.Contains(id)).ToList();
    }

    // Per-instance fan-out, run with full concurrency. The set is bounded
    // by the batch's first-seen instance count; for the typical "one new
    // flow at a time" case this is a single Flowable round trip. Failures
    // are logged and swallowed so the parent execution apply can't be
    // taken down by a single instance's flaky task fetch — the next task
    // feed tick is the safety net.
    private async Task CoalesceTasksForNewInstancesAsync(
        IReadOnlyList<string> instanceIds,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        var fetches = instanceIds.Select(async id =>
        {
            try
            {
                return await _flowable.GetTasksByProcessInstanceAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Coalesced task fetch failed for new instance {InstanceId}; the task feed will populate it on its next tick.",
                    id);
                return (IReadOnlyList<FlowableTaskSummary>)Array.Empty<FlowableTaskSummary>();
            }
        });

        var fetched = await Task.WhenAll(fetches);

        var observedAt = DateTimeOffset.UtcNow;
        var taskChanges = fetched
            .SelectMany(list => list)
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .Select(t => new ChangeEvent<FlowableTaskSummary>(
                ChangeOp.Upsert, t.Id, t, observedAt))
            .ToList();

        if (taskChanges.Count > 0)
        {
            await _taskProjection.ApplyAsync(taskChanges, db, cancellationToken);
        }
    }

    private WorkflowExecutionCache MapRow(WorkflowExecutionSummary src, DateTime now)
    {
        var processKey = ExtractProcessKey(src.ProcessDefinitionId);
        var processVersion = ExtractProcessVersion(src.ProcessDefinitionId);
        var status = NormalizeStatus(src.Status);
        var start = src.StartedAtUtc?.UtcDateTime ?? now;
        DateTime? end = status is "completed" or "cancelled" or "terminated"
            ? src.LastActivityAtUtc?.UtcDateTime
            : null;
        long? duration = end is { } e ? (long?)(e - start).TotalMilliseconds : null;

        var authTags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processkey"] = processKey,
            ["definitionkey"] = src.ProcessDefinitionId,
            ["startedby"] = src.StartUserId,
            ["status"] = status
        };

        return new WorkflowExecutionCache
        {
            FlowableInstanceId = src.Id,
            ProcessDefinitionKey = processKey ?? string.Empty,
            ProcessDefinitionId = src.ProcessDefinitionId ?? string.Empty,
            ProcessDefinitionVersion = processVersion,
            BusinessKey = null,
            TenantId = null,
            Status = status,
            StartTime = start,
            EndTime = end,
            DurationMs = duration,
            StartedBy = src.StartUserId,
            CurrentActivityId = null,
            CurrentActivityName = src.CurrentStep,
            RecordId = null,
            AuthTagsJson = JsonSerializer.Serialize(authTags),
            ProjectionVersion = _options.CurrentProjectionVersion,
            LastSyncAtUtc = now
        };
    }

    public static string? ExtractProcessKey(string? processDefinitionId)
    {
        if (string.IsNullOrEmpty(processDefinitionId)) return null;
        var sep = processDefinitionId.IndexOf(':');
        return sep > 0 ? processDefinitionId[..sep] : processDefinitionId;
    }

    private static int? ExtractProcessVersion(string? processDefinitionId)
    {
        if (string.IsNullOrEmpty(processDefinitionId)) return null;
        var parts = processDefinitionId.Split(':');
        return parts.Length >= 2 && int.TryParse(parts[1], out var v) ? v : null;
    }

    private static string NormalizeStatus(string? raw) => (raw?.ToLowerInvariant()) switch
    {
        null or "" => "active",
        "running" => "active",
        "complete" or "completed" => "completed",
        "cancelled" or "canceled" => "cancelled",
        "terminated" => "terminated",
        "suspended" => "suspended",
        var other => other
    };
}
