namespace AutoNate.Web.Services.SystemIssues;

// Topic + event-type names for the system.issues bus topic. Lifecycle events
// for the self-healing platform's issue store. Mirrors the shape of
// AuthEventTopic — one topic per cross-cutting domain, with a small static
// vocabulary of typed event names.
public static class SystemIssueEventTopic
{
    public const string TopicRoot = "system";
    public const string TopicName = "system.issues";
    public const string ResourceKind = "system_issue";
}

public static class SystemIssueEventTypes
{
    // Fired when a fresh row is inserted (occurrence_count == 1 after the
    // upsert). Re-detection of an already-open issue does NOT republish
    // opened — the recorder bumps occurrence_count silently.
    public const string Opened = "system.issue.opened";

    // Fired when an upsert hit an existing open/acknowledged row whose
    // severity changed (e.g. the backlog detector ramps from warning to
    // error as count grows).
    public const string SeverityEscalated = "system.issue.severity_escalated";

    public const string Acknowledged = "system.issue.acknowledged";

    // Manual close from the API.
    public const string Resolved = "system.issue.resolved";

    // Machine-driven close (detector saw condition cleared, or remediator
    // succeeded).
    public const string AutoResolved = "system.issue.auto_resolved";

    // Remediator dispatcher tried and failed (Phase 4 wires this in).
    public const string RemediationFailed = "system.issue.remediation_failed";
}
