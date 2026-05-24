using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Append-only projection into workflow_event_log_cache. The source's event_id
// is a stable hash of (instanceId, activityId, taskId, startTime, kind) so
// the same source event always produces the same row PK; ON CONFLICT DO
// NOTHING makes repeated emissions cheap no-ops. Updates to an event (when
// it ends after starting) get applied via a second event_id with the
// "completed" suffix — this preserves the append-only invariant the
// retention janitor depends on.
public sealed class FlowableHistoryProjection : IProjection<FlowableHistoricActivityEvent>
{
    private readonly FlowableCacheOptions _options;

    public FlowableHistoryProjection(IOptions<FlowableCacheOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "flowable.workflow_event_log_cache";

    public int Version => _options.CurrentProjectionVersion;

    public Type SourceType => typeof(FlowableHistoricActivityEvent);

    public async Task ApplyAsync(
        IReadOnlyList<ChangeEvent<FlowableHistoricActivityEvent>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var change in batch)
        {
            if (change.Op == ChangeOp.Delete || change.Source is null) continue;

            var src = change.Source;
            var processKey = FlowableExecutionProjection.ExtractProcessKey(src.ProcessDefinitionId) ?? string.Empty;

            // Two rows per activity: one for "started", one for "ended"
            // (when EndTime is present). The "ended" row carries
            // duration_ms; the "started" row is the entry point for
            // process-mining queries that need both halves.
            await InsertEventAsync(db, src, processKey, "activity_started", src.StartTime, durationMs: null, now, cancellationToken);
            if (src.EndTime is not null)
            {
                await InsertEventAsync(db, src, processKey, "activity_ended", src.EndTime, src.DurationMs, now, cancellationToken);
            }
        }
    }

    private async Task InsertEventAsync(
        AutoNateDbContext db,
        FlowableHistoricActivityEvent src,
        string processKey,
        string eventType,
        DateTimeOffset? eventTime,
        long? durationMs,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var time = eventTime?.UtcDateTime ?? now;
        var eventId = BuildEventId(src.ProcessInstanceId, src.ActivityId, src.TaskId, eventType, time);
        var payload = JsonSerializer.Serialize(new
        {
            src.DeleteReason,
            assignee = src.Assignee,
            activityType = src.ActivityType
        });

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO workflow_event_log_cache (
                event_id, flowable_instance_id, process_definition_key,
                event_time, event_type, activity_id, activity_name, activity_type,
                task_id, variable_name, actor, duration_ms,
                payload, projection_version, last_sync_at)
            VALUES (
                {eventId}, {src.ProcessInstanceId}, {processKey},
                {time}, {eventType}, {src.ActivityId}, {src.ActivityName}, {src.ActivityType},
                {src.TaskId}, {(string?)null}, {src.Assignee}, {durationMs},
                {payload}::jsonb, {_options.CurrentProjectionVersion}, {now})
            ON CONFLICT (event_id) DO NOTHING
            """, cancellationToken);
    }

    // Stable hash gives idempotency without per-row UUID generation. SHA-256
    // truncated to 32 hex chars is well below collision risk for the row
    // volumes we expect (hundreds of millions over 7 years) and avoids the
    // database having to assign primary keys server-side.
    private static string BuildEventId(string instanceId, string? activityId, string? taskId, string eventType, DateTime time)
    {
        var seed = $"{instanceId}|{activityId}|{taskId}|{eventType}|{time:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var sb = new StringBuilder(32);
        for (var i = 0; i < 16; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
