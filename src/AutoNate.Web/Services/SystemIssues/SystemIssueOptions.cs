namespace AutoNate.Web.Services.SystemIssues;

public sealed class SystemIssueOptions
{
    public const string SectionName = "SystemIssues";

    // Master switch for all detectors. Tests set this false in
    // appsettings.Test.json so detector hosted services don't tick during
    // integration runs. Mirrors AuditOutboxOptions.Enabled discipline.
    public bool DetectorsEnabled { get; set; } = true;

    // Master switch for the remediator dispatcher. Independent of detectors —
    // an operator can record but disable auto-fix during a fire if needed.
    public bool RemediationEnabled { get; set; } = true;

    public TimeSpan RemediationPollInterval { get; set; } = TimeSpan.FromSeconds(10);

    public int RemediationBatchSize { get; set; } = 25;

    // Backoff is exponential, capped: attempt N waits min(BaseBackoff * 2^N, MaxBackoff).
    public TimeSpan RemediationBaseBackoff { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RemediationMaxBackoff { get; set; } = TimeSpan.FromMinutes(30);

    // After this many failed remediation attempts, the dispatcher stops
    // retrying and the issue stays open for human triage. Mirrors
    // AuditOutboxOptions.MaxAttempts but tighter — a remediator that fails 3
    // times in a row is almost certainly hitting a real obstacle.
    public int MaxRemediationAttempts { get; set; } = 3;
}
