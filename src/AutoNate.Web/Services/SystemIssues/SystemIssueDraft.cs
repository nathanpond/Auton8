namespace AutoNate.Web.Services.SystemIssues;

// What detectors hand to ISystemIssueRecorder. Fingerprint is the dedup key —
// pick something stable per real-world condition (e.g.
// "audit_outbox:dead_letter:" + outboxRowId, or "health:component:postgres").
//
// FactsJson must carry IDs/counts/error messages, never row payloads (mirrors
// the EventCatalog ViewEventPayloadFields PII discipline).
//
// RemediationDueAtUtc is the opt-in for the dispatcher: detectors that know
// their issue class is safely auto-remediable set it (typically to NOW so the
// next dispatcher tick picks it up). Detectors for not-safely-auto-remediable
// classes (stuck workflows, auth bursts) leave it null and the dispatcher
// never schedules them. The recorder writes this field only on INSERT so
// dedup-bumps don't reset the dispatcher's exponential backoff.
public sealed record SystemIssueDraft(
    string DetectorId,
    string Category,
    string Severity,
    string Fingerprint,
    string Title,
    string? Summary = null,
    string? RelatedEntityKind = null,
    string? RelatedEntityId = null,
    string FactsJson = "{}",
    DateTime? RemediationDueAtUtc = null);
