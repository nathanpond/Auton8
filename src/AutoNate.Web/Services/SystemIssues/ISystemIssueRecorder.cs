namespace AutoNate.Web.Services.SystemIssues;

// All detectors and remediators talk to the issue store through this surface
// so they don't take an EF dependency. The store is responsible for the
// upsert-by-fingerprint dedup contract — callers just call RecordAsync and
// don't worry about whether they're creating or bumping.
public interface ISystemIssueRecorder
{
    // Create a new open issue, or bump occurrence_count + last_seen_at_utc on
    // the existing open/acknowledged row with the same fingerprint. Returns
    // the row's id (existing or new) and whether it was newly created.
    Task<RecordIssueResult> RecordAsync(SystemIssueDraft draft, CancellationToken cancellationToken = default);

    // Detector-driven resolution: the condition is no longer present (e.g.
    // SystemHealthSnapshotDetector saw the component come back up). Idempotent
    // — if the issue is already resolved, returns null without changing state.
    Task<SystemIssue?> MarkResolvedByFingerprintAsync(
        string fingerprint,
        string resolutionKind,
        string? notes,
        CancellationToken cancellationToken = default);

    // Operator hit POST /acknowledge — flips state to acknowledged so it
    // doesn't show in default "open" lists but stays visible. Returns null
    // if the issue isn't found, or if it's already resolved (acking a
    // resolved issue is a no-op).
    Task<SystemIssue?> AcknowledgeAsync(
        Guid issueId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    // Operator hit POST /resolve — manual closure with notes. State =
    // 'resolved' (vs auto_resolved which is for machine actions).
    Task<SystemIssue?> ResolveAsync(
        Guid issueId,
        Guid actorUserId,
        string? notes,
        CancellationToken cancellationToken = default);
}

// `WasCreated` is true on a fresh insert; `PreviousSeverity` is the severity
// the row had before this RecordAsync call (null when this call inserted it).
// Together they tell the recorder whether to publish system.issue.opened or
// system.issue.severity_escalated.
public sealed record RecordIssueResult(
    Guid IssueId,
    bool WasCreated,
    int OccurrenceCount,
    string? PreviousSeverity = null);
