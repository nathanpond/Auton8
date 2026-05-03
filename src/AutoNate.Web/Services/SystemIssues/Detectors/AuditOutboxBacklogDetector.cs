using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Watches the audit_outbox for backlog: rows that have been sitting
// undispatched (and not yet abandoned at MaxAttempts) longer than the
// configured threshold. A growing backlog usually means the dispatcher is
// healthy but the downstream (Dapr → NATS) is rejecting publishes — so the
// rows accumulate. Severity escalates with the count so an operator triages
// at the right urgency.
//
// Dead-lettered rows (attempt_count >= MaxAttempts) are out of scope here —
// AuditOutboxDeadLetterDetector handles those one-by-one.
public sealed class AuditOutboxBacklogDetector(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ISystemIssueRecorder recorder,
    IOptions<AuditOutboxBacklogDetectorOptions> backlogOptions,
    IOptions<AuditOutboxOptions> auditOutboxOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<AuditOutboxBacklogDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly AuditOutboxBacklogDetectorOptions _backlogOptions = backlogOptions.Value;
    private readonly AuditOutboxOptions _auditOutboxOptions = auditOutboxOptions.Value;

    public const string DetectorIdValue = "audit_outbox_backlog";
    private const string Fingerprint = "audit_outbox:backlog";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _backlogOptions.Interval;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var staleSince = DateTime.UtcNow - _backlogOptions.StaleAfter;
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var maxAttempts = _auditOutboxOptions.MaxAttempts;
        var backlogCount = await dbContext.AuditOutbox.AsNoTracking()
            .Where(r => r.DispatchedAtUtc == null
                     && r.AttemptCount < maxAttempts
                     && r.CreatedAtUtc < staleSince)
            .CountAsync(cancellationToken);

        if (backlogCount == 0)
        {
            await recorder.MarkResolvedByFingerprintAsync(
                Fingerprint,
                SystemIssueResolutionKinds.NoLongerPresent,
                notes: "Audit outbox backlog cleared.",
                cancellationToken);
            return;
        }

        var severity = ClassifySeverity(backlogCount, _backlogOptions);
        var oldestCreatedAt = await dbContext.AuditOutbox.AsNoTracking()
            .Where(r => r.DispatchedAtUtc == null
                     && r.AttemptCount < maxAttempts
                     && r.CreatedAtUtc < staleSince)
            .MinAsync(r => (DateTime?)r.CreatedAtUtc, cancellationToken);

        var facts = JsonSerializer.Serialize(new
        {
            backlogCount,
            staleAfterSeconds = (int)_backlogOptions.StaleAfter.TotalSeconds,
            oldestCreatedAtUtc = oldestCreatedAt,
            maxAttempts,
            warningThreshold = _backlogOptions.WarningAtCount,
            errorThreshold = _backlogOptions.ErrorAtCount,
            criticalThreshold = _backlogOptions.CriticalAtCount
        });

        await recorder.RecordAsync(new SystemIssueDraft(
            DetectorId: DetectorIdValue,
            Category: SystemIssueCategories.Bus,
            Severity: severity,
            Fingerprint: Fingerprint,
            Title: $"Audit outbox backlog: {backlogCount} undispatched rows",
            Summary: $"{backlogCount} audit_outbox rows have been undispatched for longer than {(int)_backlogOptions.StaleAfter.TotalSeconds}s. Dispatcher may be running, but downstream is not accepting publishes.",
            RelatedEntityKind: "audit_outbox",
            RelatedEntityId: null,
            FactsJson: facts), cancellationToken);
    }

    // Public for tests — pure function over the configured ladder.
    public static string ClassifySeverity(int count, AuditOutboxBacklogDetectorOptions opts)
    {
        if (count >= opts.CriticalAtCount) return SystemIssueSeverities.Critical;
        if (count >= opts.ErrorAtCount) return SystemIssueSeverities.Error;
        if (count >= opts.WarningAtCount) return SystemIssueSeverities.Warning;
        return SystemIssueSeverities.Info;
    }
}

public sealed class AuditOutboxBacklogDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:AuditOutboxBacklog";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    // A row only counts as "backlog" once it's been undispatched longer than
    // this. Stops the detector from flagging the natural couple-second window
    // between insert and dispatch.
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    // Severity ladder. Defaults are conservative — tune via configuration
    // once we have real-world numbers from a deployment.
    public int WarningAtCount { get; set; } = 50;
    public int ErrorAtCount { get; set; } = 500;
    public int CriticalAtCount { get; set; } = 5_000;
}
