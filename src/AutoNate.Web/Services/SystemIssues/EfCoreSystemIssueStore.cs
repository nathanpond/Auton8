using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SystemIssueEntity = AutoNate.Web.Persistence.Scaffolded.SystemIssue;

namespace AutoNate.Web.Services.SystemIssues;

// EF Core + raw SQL hybrid. The upsert path goes through a raw NpgsqlCommand
// because the partial unique index on (fingerprint) WHERE state IN
// ('open','acknowledged') can't be expressed via EF's tracked-entity
// SaveChanges path — we need `ON CONFLICT (fingerprint) WHERE ...` with the
// matching predicate so the index inference resolves.
//
// Read paths and the simpler state-transition mutators use EF Core directly.
//
// Phase 3: every state-changing operation publishes a lifecycle event on the
// system.issues topic via IAuditEventPublisher (which routes through the
// audit_outbox so a Dapr/NATS hiccup doesn't drop the event).
public sealed class EfCoreSystemIssueStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IAuditEventPublisher auditPublisher,
    ICriticalIssueNotifier criticalNotifier) : ISystemIssueRecorder, ISystemIssueStore
{
    public async Task<RecordIssueResult> RecordAsync(SystemIssueDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Capture the prior severity in a CTE so we can detect severity
        // escalation on the same round-trip as the upsert. The CTE evaluates
        // before the INSERT runs, so it sees the pre-upsert state. RETURNING
        // also exposes whether this was an insert (occurrence_count == 1).
        const string sql = """
            WITH prior AS (
                SELECT severity AS prev_severity
                FROM system_issues
                WHERE fingerprint = @fingerprint
                  AND state IN ('open', 'acknowledged')
            )
            INSERT INTO system_issues (
                detector_id, category, severity, fingerprint, title, summary,
                related_entity_kind, related_entity_id, facts_json,
                next_remediation_after_utc
            ) VALUES (
                @detector_id, @category, @severity, @fingerprint, @title, @summary,
                @related_entity_kind, @related_entity_id, @facts_json::jsonb,
                @next_remediation_after_utc
            )
            ON CONFLICT (fingerprint) WHERE state IN ('open', 'acknowledged')
            DO UPDATE SET
                occurrence_count = system_issues.occurrence_count + 1,
                last_seen_at_utc = NOW(),
                severity = EXCLUDED.severity,
                title = EXCLUDED.title,
                summary = EXCLUDED.summary,
                facts_json = EXCLUDED.facts_json
                -- Intentionally NOT bumping next_remediation_after_utc on
                -- update: the dispatcher owns the backoff schedule once it
                -- starts working a row, and a re-detection in the middle of
                -- exponential backoff shouldn't reset the timer.
            RETURNING id, occurrence_count, (SELECT prev_severity FROM prior) AS prev_severity;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("detector_id", draft.DetectorId);
        command.Parameters.AddWithValue("category", draft.Category);
        command.Parameters.AddWithValue("severity", draft.Severity);
        command.Parameters.AddWithValue("fingerprint", draft.Fingerprint);
        command.Parameters.AddWithValue("title", draft.Title);
        command.Parameters.AddWithValue("summary", (object?)draft.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("related_entity_kind", (object?)draft.RelatedEntityKind ?? DBNull.Value);
        command.Parameters.AddWithValue("related_entity_id", (object?)draft.RelatedEntityId ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("facts_json", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(draft.FactsJson) ? "{}" : draft.FactsJson
        });
        command.Parameters.Add(new NpgsqlParameter("next_remediation_after_utc", NpgsqlDbType.TimestampTz)
        {
            Value = (object?)draft.RemediationDueAtUtc ?? DBNull.Value
        });

        Guid id;
        int occurrenceCount;
        string? previousSeverity;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("system_issues upsert returned no row.");
            }
            id = reader.GetGuid(0);
            occurrenceCount = reader.GetInt32(1);
            previousSeverity = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        var wasCreated = occurrenceCount == 1;
        var result = new RecordIssueResult(id, wasCreated, occurrenceCount, previousSeverity);
        await PublishLifecycleAsync(result, draft, cancellationToken);
        if (wasCreated)
        {
            // Notifier checks severity itself and no-ops below error/critical
            // — keeps the call site here a single line.
            await criticalNotifier.NotifyOpenedAsync(
                id, draft.Severity, draft.Title, draft.Summary, cancellationToken);
        }
        return result;
    }

    private async Task PublishLifecycleAsync(
        RecordIssueResult result,
        SystemIssueDraft draft,
        CancellationToken cancellationToken)
    {
        if (result.WasCreated)
        {
            await auditPublisher.PublishAsync(
                SystemIssueEventTopic.TopicName,
                SystemIssueEventTypes.Opened,
                SystemIssueEventTopic.ResourceKind,
                resource: new
                {
                    id = result.IssueId,
                    fingerprint = draft.Fingerprint,
                    detectorId = draft.DetectorId,
                    category = draft.Category,
                    severity = draft.Severity,
                    title = draft.Title
                },
                details: new
                {
                    relatedEntityKind = draft.RelatedEntityKind,
                    relatedEntityId = draft.RelatedEntityId,
                    summary = draft.Summary
                },
                cancellationToken);
            return;
        }

        // Existing row: only republish if severity changed. Pure occurrence-
        // count bumps would otherwise dominate the audit firehose for a
        // chronic issue (e.g. flapping connection ticking once a minute).
        if (!string.Equals(result.PreviousSeverity, draft.Severity, StringComparison.Ordinal))
        {
            await auditPublisher.PublishAsync(
                SystemIssueEventTopic.TopicName,
                SystemIssueEventTypes.SeverityEscalated,
                SystemIssueEventTopic.ResourceKind,
                resource: new
                {
                    id = result.IssueId,
                    fingerprint = draft.Fingerprint,
                    detectorId = draft.DetectorId,
                    severity = draft.Severity,
                    title = draft.Title
                },
                details: new
                {
                    previousSeverity = result.PreviousSeverity,
                    occurrenceCount = result.OccurrenceCount
                },
                cancellationToken);
        }
    }

    public async Task<SystemIssue?> MarkResolvedByFingerprintAsync(
        string fingerprint,
        string resolutionKind,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await dbContext.SystemIssues
            .FirstOrDefaultAsync(
                i => i.Fingerprint == fingerprint
                  && (i.State == SystemIssueStates.Open || i.State == SystemIssueStates.Acknowledged),
                cancellationToken);
        if (row is null)
        {
            return null;
        }

        // Manual = human via API; everything else is a machine action
        // (detector saying "no longer present", remediator running). Machine
        // actions land in auto_resolved so the SPA filter "Resolved (manual)"
        // vs "Auto-resolved (machine)" stays meaningful.
        row.State = resolutionKind == SystemIssueResolutionKinds.Manual
            ? SystemIssueStates.Resolved
            : SystemIssueStates.AutoResolved;
        row.ResolutionKind = resolutionKind;
        row.ResolutionNotes = notes;
        row.ResolvedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        var domain = ToDomain(row);

        var eventType = row.State == SystemIssueStates.Resolved
            ? SystemIssueEventTypes.Resolved
            : SystemIssueEventTypes.AutoResolved;
        await auditPublisher.PublishAsync(
            SystemIssueEventTopic.TopicName,
            eventType,
            SystemIssueEventTopic.ResourceKind,
            resource: new { id = row.Id, fingerprint = row.Fingerprint, severity = row.Severity },
            details: new { resolutionKind, notes },
            cancellationToken);
        return domain;
    }

    public async Task<SystemIssue?> AcknowledgeAsync(
        Guid issueId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await dbContext.SystemIssues
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);
        if (row is null || row.State != SystemIssueStates.Open)
        {
            // Already acknowledged or already resolved — no-op. The endpoint
            // turns null into 404/409 as appropriate.
            return null;
        }

        row.State = SystemIssueStates.Acknowledged;
        row.AcknowledgedAtUtc = DateTime.UtcNow;
        row.AcknowledgedBy = actorUserId;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            SystemIssueEventTopic.TopicName,
            SystemIssueEventTypes.Acknowledged,
            SystemIssueEventTopic.ResourceKind,
            resource: new { id = row.Id, fingerprint = row.Fingerprint, severity = row.Severity },
            details: new { acknowledgedBy = actorUserId },
            cancellationToken);
        return ToDomain(row);
    }

    public async Task<SystemIssue?> ResolveAsync(
        Guid issueId,
        Guid actorUserId,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await dbContext.SystemIssues
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);
        if (row is null || row.State is SystemIssueStates.Resolved or SystemIssueStates.AutoResolved)
        {
            return null;
        }

        row.State = SystemIssueStates.Resolved;
        row.ResolutionKind = SystemIssueResolutionKinds.Manual;
        row.ResolutionNotes = notes;
        row.ResolvedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            SystemIssueEventTopic.TopicName,
            SystemIssueEventTypes.Resolved,
            SystemIssueEventTopic.ResourceKind,
            resource: new { id = row.Id, fingerprint = row.Fingerprint, severity = row.Severity },
            details: new { resolvedBy = actorUserId, notes },
            cancellationToken);
        return ToDomain(row);
    }

    public async Task<IReadOnlyList<SystemIssue>> ListAsync(
        SystemIssueListQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var q = dbContext.SystemIssues.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(query.State)) q = q.Where(i => i.State == query.State);
        if (!string.IsNullOrEmpty(query.Severity)) q = q.Where(i => i.Severity == query.Severity);
        if (!string.IsNullOrEmpty(query.Category)) q = q.Where(i => i.Category == query.Category);

        var rows = await q
            .OrderByDescending(i => i.LastSeenAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return rows.ConvertAll(ToDomain);
    }

    public async Task<SystemIssue?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await dbContext.SystemIssues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<string>> ListOpenFingerprintsForDetectorAsync(
        string detectorId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SystemIssues.AsNoTracking()
            .Where(i => i.DetectorId == detectorId
                     && (i.State == SystemIssueStates.Open
                         || i.State == SystemIssueStates.Acknowledged))
            .Select(i => i.Fingerprint)
            .ToListAsync(cancellationToken);
    }

    private static SystemIssue ToDomain(SystemIssueEntity row) => new(
        row.Id,
        row.DetectorId,
        row.Category,
        row.Severity,
        row.Fingerprint,
        row.Title,
        row.Summary,
        row.RelatedEntityKind,
        row.RelatedEntityId,
        row.FactsJson,
        row.State,
        new DateTimeOffset(DateTime.SpecifyKind(row.FirstSeenAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(row.LastSeenAtUtc, DateTimeKind.Utc)),
        row.OccurrenceCount,
        row.AcknowledgedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.AcknowledgedAtUtc.Value, DateTimeKind.Utc))
            : null,
        row.AcknowledgedBy,
        row.ResolvedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.ResolvedAtUtc.Value, DateTimeKind.Utc))
            : null,
        row.ResolutionKind,
        row.ResolutionNotes,
        row.AutoRemediationAttemptCount,
        row.AutoRemediationLastError,
        row.NextRemediationAfterUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.NextRemediationAfterUtc.Value, DateTimeKind.Utc))
            : null);
}
