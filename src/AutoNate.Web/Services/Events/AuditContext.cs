namespace AutoNate.Web.Services.Events;

// Embedded in every event envelope so a downstream audit consumer has the
// minimum context needed to answer "who did what, when, from where, and was
// it allowed?" without joining back to the request log. New event envelopes
// should carry this nested record. Existing envelopes were retrofitted in
// Phase 1 of the audit-events plan; their legacy top-level actor/timestamp
// fields are kept for one release for back-compat and will be removed once
// every consumer reads from auditContext.
public sealed record AuditContext(
    Guid? ActorId,
    string? ActorUserName,
    DateTimeOffset OccurredAtUtc,
    string RequestId,
    string? CorrelationId,
    string IpAddress,
    string UserAgent,
    string SourceAppId,
    string HttpMethod,
    string RoutePath,
    AuthOutcome AuthOutcome,
    string? AuthDecisionReason);

public enum AuthOutcome
{
    // The request was permitted. Set on every successful mutation/view event.
    Allowed,
    // The request was rejected by the authorization layer. AuthDecisionReason
    // carries the human-readable reason from AuthDecision.Reason.
    Denied,
    // The request never presented a valid identity (e.g. failed login attempt,
    // anonymous access to a protected resource). ActorId will be null.
    Anonymous
}
