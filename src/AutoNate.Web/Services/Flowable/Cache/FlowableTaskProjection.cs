using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Translates FlowableTaskSummary → workflow_task_cache rows. Mirrors the
// execution projection's shape; both share the upsert + auth-tag pattern.
//
// Candidate user/group enrichment requires a follow-up Flowable
// identity-link fetch which isn't exposed on FlowableTaskSummary today —
// see FlowableInstanceAuthorizers.cs:67 for the same gap. For Phase 1
// candidate arrays stay empty and selector grants of the form
// `[candidategroup=X]` won't match cached rows (matching pre-cache
// behavior).
public sealed class FlowableTaskProjection : IProjection<FlowableTaskSummary>
{
    private readonly FlowableCacheOptions _options;

    public FlowableTaskProjection(IOptions<FlowableCacheOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "flowable.workflow_task_cache";

    public int Version => _options.CurrentProjectionVersion;

    public Type SourceType => typeof(FlowableTaskSummary);

    public async Task ApplyAsync(
        IReadOnlyList<ChangeEvent<FlowableTaskSummary>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        var latest = new Dictionary<string, ChangeEvent<FlowableTaskSummary>>(StringComparer.Ordinal);
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

        var now = DateTime.UtcNow;
        foreach (var change in latest.Values)
        {
            var row = MapRow(change.Source!, now);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workflow_task_cache (
                    flowable_task_id, flowable_instance_id, process_definition_key,
                    task_definition_key, name, assignee, owner,
                    candidate_users, candidate_groups, due_date,
                    created_time, claim_time, completed_time, form_key, priority,
                    status, auth_tags, projection_version, last_sync_at)
                VALUES (
                    {row.FlowableTaskId}, {row.FlowableInstanceId}, {row.ProcessDefinitionKey},
                    {row.TaskDefinitionKey}, {row.Name}, {row.Assignee}, {row.Owner},
                    {row.CandidateUsers}::text[], {row.CandidateGroups}::text[], {row.DueDate},
                    {row.CreatedTime}, {row.ClaimTime}, {row.CompletedTime}, {row.FormKey}, {row.Priority},
                    {row.Status}, {row.AuthTagsJson}::jsonb, {row.ProjectionVersion}, {row.LastSyncAtUtc})
                ON CONFLICT (flowable_task_id) DO UPDATE SET
                    flowable_instance_id   = EXCLUDED.flowable_instance_id,
                    process_definition_key = EXCLUDED.process_definition_key,
                    task_definition_key    = EXCLUDED.task_definition_key,
                    name                   = EXCLUDED.name,
                    assignee               = EXCLUDED.assignee,
                    owner                  = EXCLUDED.owner,
                    candidate_users        = EXCLUDED.candidate_users,
                    candidate_groups       = EXCLUDED.candidate_groups,
                    due_date               = EXCLUDED.due_date,
                    created_time           = EXCLUDED.created_time,
                    claim_time             = EXCLUDED.claim_time,
                    -- #629. COMPLETION IS A POSITIVE FACT AND SURVIVES A RE-POLL.
                    --
                    -- Every producer emits only OPEN tasks, so MapRow writes
                    -- completed_time = null and status = 'active' unconditionally.
                    -- That is safe in the steady state and not safe in a race:
                    -- FlowableTaskPollingFeed emits into a channel this projection
                    -- drains asynchronously, so a page captured BEFORE a completion
                    -- can be applied AFTER WorkflowTaskCompletionSweep marked it,
                    -- resetting the row to active/NULL.
                    --
                    -- Recovery was not guaranteed either: the sweep walks
                    -- endTime DESC and breaks on the first page that marks nothing,
                    -- so once newer completions push the resurrected task off page
                    -- 0 it is never re-marked, and CURRENTSTEP() goes back to
                    -- reporting the stale task -- the symptom #586 exists to remove.
                    --
                    -- COALESCE keeps the first completion anyone observed. Both
                    -- writers already guard on `completed_time IS NULL` for the
                    -- same reason; this is that rule applied to the poll path,
                    -- which had no guard at all.
                    completed_time         = COALESCE(
                                                 workflow_task_cache.completed_time,
                                                 EXCLUDED.completed_time),
                    form_key               = EXCLUDED.form_key,
                    priority               = EXCLUDED.priority,
                    status                 = CASE
                                                 WHEN workflow_task_cache.completed_time IS NOT NULL
                                                 THEN workflow_task_cache.status
                                                 ELSE EXCLUDED.status
                                             END,
                    auth_tags              = EXCLUDED.auth_tags,
                    projection_version     = EXCLUDED.projection_version,
                    last_sync_at           = EXCLUDED.last_sync_at
                """, cancellationToken);
        }

        foreach (var id in deletes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workflow_task_cache WHERE flowable_task_id = {id}",
                cancellationToken);
        }
    }

    private WorkflowTaskCache MapRow(FlowableTaskSummary src, DateTime now)
    {
        var processKey = FlowableExecutionProjection.ExtractProcessKey(src.ProcessDefinitionId) ?? string.Empty;
        var authTags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processkey"] = processKey,
            ["definitionkey"] = src.ProcessDefinitionId,
            ["assignee"] = src.Assignee,
            ["status"] = "active"
        };

        return new WorkflowTaskCache
        {
            FlowableTaskId = src.Id,
            FlowableInstanceId = src.ProcessInstanceId ?? string.Empty,
            ProcessDefinitionKey = processKey,
            TaskDefinitionKey = src.TaskDefinitionKey,
            Name = src.Name,
            Assignee = src.Assignee,
            Owner = null,
            CandidateUsers = Array.Empty<string>(),
            CandidateGroups = Array.Empty<string>(),
            DueDate = src.DueDate?.UtcDateTime,
            CreatedTime = src.CreatedAtUtc?.UtcDateTime ?? now,
            ClaimTime = null,
            CompletedTime = null,
            FormKey = null,
            Priority = null,
            Status = "active",
            AuthTagsJson = JsonSerializer.Serialize(authTags),
            ProjectionVersion = _options.CurrentProjectionVersion,
            LastSyncAtUtc = now
        };
    }
}
