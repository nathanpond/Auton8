using System.Text.Json;
using AutoNate.Web.Services.SystemHealth;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Turns SystemHealthService's point-in-time probe into a persistent timeline
// of dependency outages. On each tick:
//
// * For every component that is Down or Degraded, record an issue keyed
//   `health:component:{id}`. Re-detection bumps occurrence_count; severity
//   tracks the current status (Degraded → warning, Down → error).
// * For every connection in the same states, record `health:connection:{from}->{to}`.
// * For everything that is Up but matches an open `health:` issue, mark it
//   resolved with kind `no_longer_present`.
//
// This is the single source of truth for outage state — the AuditOutbox
// detectors specifically don't open their own "Postgres unreachable" issues
// because this detector's resolution would race with theirs.
public sealed class SystemHealthSnapshotDetector(
    ISystemHealthProbe healthService,
    ISystemIssueRecorder recorder,
    ISystemIssueStore issueStore,
    IOptions<SystemHealthSnapshotOptions> snapshotOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<SystemHealthSnapshotDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly SystemHealthSnapshotOptions _snapshotOptions = snapshotOptions.Value;

    public const string DetectorIdValue = "system_health_snapshot";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _snapshotOptions.Interval;

    // Probe immediately on startup so an outage that's already in progress
    // when the app comes back up shows up in the first minute.
    protected override TimeSpan InitialStagger() => TimeSpan.Zero;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var report = await healthService.CheckAsync(cancellationToken);
        await ProcessReportAsync(report, cancellationToken);
    }

    // Public so tests can drive a single processing pass with a constructed
    // report (no need to start the BackgroundService loop).
    public async Task ProcessReportAsync(SystemHealthReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        var seenThisTick = new HashSet<string>(StringComparer.Ordinal);

        foreach (var component in report.Components)
        {
            if (component.Status is HealthStatus.Down or HealthStatus.Degraded)
            {
                var fingerprint = $"health:component:{component.Id}";
                seenThisTick.Add(fingerprint);
                await recorder.RecordAsync(BuildComponentDraft(component, fingerprint), cancellationToken);
            }
        }

        foreach (var connection in report.Connections)
        {
            if (connection.Status is HealthStatus.Down or HealthStatus.Degraded)
            {
                var fingerprint = $"health:connection:{connection.From}->{connection.To}";
                seenThisTick.Add(fingerprint);
                await recorder.RecordAsync(BuildConnectionDraft(connection, fingerprint), cancellationToken);
            }
        }

        // Auto-resolve via DB query — survives app restarts, unlike the
        // in-memory `_openFingerprints` set used previously which left
        // stale issues open whenever the host bounced between an outage
        // and its recovery.
        var openInDb = await issueStore.ListOpenFingerprintsForDetectorAsync(DetectorIdValue, cancellationToken);
        foreach (var fingerprint in openInDb)
        {
            if (seenThisTick.Contains(fingerprint)) continue;
            await recorder.MarkResolvedByFingerprintAsync(
                fingerprint,
                SystemIssueResolutionKinds.NoLongerPresent,
                notes: "Component/connection reported Up on subsequent probe.",
                cancellationToken);
        }
    }

    private static SystemIssueDraft BuildComponentDraft(ComponentHealth component, string fingerprint)
    {
        var severity = component.Status == HealthStatus.Down
            ? SystemIssueSeverities.Error
            : SystemIssueSeverities.Warning;

        var facts = JsonSerializer.Serialize(new
        {
            componentId = component.Id,
            componentName = component.Name,
            kind = component.Kind,
            status = component.Status.ToString(),
            message = component.Message,
            latencyMs = component.LatencyMs,
            details = component.Details
        });

        var title = component.Status == HealthStatus.Down
            ? $"{component.Name} is Down"
            : $"{component.Name} is Degraded";

        return new SystemIssueDraft(
            DetectorId: DetectorIdValue,
            Category: SystemIssueCategories.Resource,
            Severity: severity,
            Fingerprint: fingerprint,
            Title: title,
            Summary: component.Message,
            RelatedEntityKind: "system_health_component",
            RelatedEntityId: component.Id,
            FactsJson: facts);
    }

    private static SystemIssueDraft BuildConnectionDraft(ConnectionHealth connection, string fingerprint)
    {
        var severity = connection.Status == HealthStatus.Down
            ? SystemIssueSeverities.Error
            : SystemIssueSeverities.Warning;

        var facts = JsonSerializer.Serialize(new
        {
            from = connection.From,
            to = connection.To,
            label = connection.Label,
            status = connection.Status.ToString(),
            message = connection.Message,
            latencyMs = connection.LatencyMs
        });

        var title = connection.Status == HealthStatus.Down
            ? $"{connection.From} → {connection.To} ({connection.Label}) is Down"
            : $"{connection.From} → {connection.To} ({connection.Label}) is Degraded";

        return new SystemIssueDraft(
            DetectorId: DetectorIdValue,
            Category: SystemIssueCategories.Resource,
            Severity: severity,
            Fingerprint: fingerprint,
            Title: title,
            Summary: connection.Message,
            RelatedEntityKind: "system_health_connection",
            RelatedEntityId: $"{connection.From}->{connection.To}",
            FactsJson: facts);
    }
}

public sealed class SystemHealthSnapshotOptions
{
    public const string SectionName = "SystemIssues:Detectors:SystemHealthSnapshot";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
