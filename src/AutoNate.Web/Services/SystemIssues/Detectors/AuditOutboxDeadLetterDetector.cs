using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Surfaces every audit_outbox row the dispatcher has given up on. The
// dispatcher already abandons rows where attempt_count >= MaxAttempts; this
// detector makes those abandoned rows visible to operators and gives the
// remediation framework (Phase 4) a hook to park them safely.
//
// One issue per row, fingerprint = "audit_outbox:dead_letter:{id}". The row's
// id is stable, so re-running the detector dedups; if a future operation
// successfully dispatches the row (unlikely without intervention) this
// detector simply stops re-detecting it on the next tick — the open issue
// stays until manually resolved or auto-remediated.
public sealed class AuditOutboxDeadLetterDetector(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ISystemIssueRecorder recorder,
    IOptions<AuditOutboxDeadLetterDetectorOptions> deadLetterOptions,
    IOptions<AuditOutboxOptions> auditOutboxOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<AuditOutboxDeadLetterDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly AuditOutboxDeadLetterDetectorOptions _deadLetterOptions = deadLetterOptions.Value;
    private readonly AuditOutboxOptions _auditOutboxOptions = auditOutboxOptions.Value;

    public const string DetectorIdValue = "audit_outbox_dead_letter";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _deadLetterOptions.Interval;

    // Smaller initial stagger than the base — when the app starts up there
    // may already be dead letters from before the previous shutdown, and we
    // want them visible quickly.
    protected override TimeSpan InitialStagger() => TimeSpan.FromSeconds(15);

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var maxAttempts = _auditOutboxOptions.MaxAttempts;
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var deadLetters = await dbContext.AuditOutbox.AsNoTracking()
            .Where(r => r.DispatchedAtUtc == null && r.AttemptCount >= maxAttempts)
            .OrderBy(r => r.Id)
            .Take(_deadLetterOptions.BatchSize)
            .ToListAsync(cancellationToken);

        if (deadLetters.Count == 0)
        {
            return;
        }

        foreach (var row in deadLetters)
        {
            var fingerprint = $"audit_outbox:dead_letter:{row.Id}";
            var facts = JsonSerializer.Serialize(new
            {
                outboxRowId = row.Id,
                topic = row.Topic,
                eventType = row.EventType,
                attemptCount = row.AttemptCount,
                maxAttempts,
                createdAtUtc = row.CreatedAtUtc,
                lastError = row.LastError
            });

            await recorder.RecordAsync(new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.Bus,
                Severity: SystemIssueSeverities.Error,
                Fingerprint: fingerprint,
                Title: $"Audit outbox dead-letter: {row.Topic}/{row.EventType}",
                Summary: row.LastError is null
                    ? $"Row {row.Id} reached MaxAttempts ({maxAttempts}) without dispatching."
                    : $"Row {row.Id} reached MaxAttempts ({maxAttempts}). Last error: {row.LastError}",
                RelatedEntityKind: "audit_outbox",
                RelatedEntityId: row.Id.ToString(CultureInfo.InvariantCulture),
                FactsJson: facts,
                // Opt this issue into auto-remediation immediately. The
                // dispatcher's next tick will pick it up and route to
                // AuditOutboxDeadLetterParkRemediator. (Field set on insert
                // only — re-detection bumps occurrence_count without
                // touching the dispatcher's backoff schedule.)
                RemediationDueAtUtc: DateTime.UtcNow), cancellationToken);
        }
    }
}

public sealed class AuditOutboxDeadLetterDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:AuditOutboxDeadLetter";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    // Per-tick cap. A pathological event storm could leave thousands of
    // dead letters; we don't want one tick to take minutes processing them
    // all. Subsequent ticks pick up the rest in id order.
    public int BatchSize { get; set; } = 500;
}
