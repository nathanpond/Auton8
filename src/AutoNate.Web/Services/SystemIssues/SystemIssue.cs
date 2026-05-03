namespace AutoNate.Web.Services.SystemIssues;

// Domain projection of the system_issues row. Separate from the EF entity in
// Persistence/Scaffolded/SystemIssue.cs so callers (endpoints, remediators,
// detectors) don't take a hard dep on EF types. Build via SystemIssue.From(...)
// from the EF entity.
public sealed record SystemIssue(
    Guid Id,
    string DetectorId,
    string Category,
    string Severity,
    string Fingerprint,
    string Title,
    string? Summary,
    string? RelatedEntityKind,
    string? RelatedEntityId,
    string FactsJson,
    string State,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int OccurrenceCount,
    DateTimeOffset? AcknowledgedAtUtc,
    Guid? AcknowledgedBy,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionKind,
    string? ResolutionNotes,
    int AutoRemediationAttemptCount,
    string? AutoRemediationLastError,
    DateTimeOffset? NextRemediationAfterUtc);
