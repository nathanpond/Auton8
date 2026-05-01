namespace AutoNate.Plugins.Abstractions;

// Plugin-facing snapshot of an audit event the host is about to publish on
// the bus. Fired as a sync/async action on HookPoints.AuditEventPublished
// after the host has built the envelope and enqueued it to the audit outbox.
// Plugins receive a flat, allocation-friendly DTO instead of the host's
// internal envelope type, so they don't need to share any types beyond the
// abstractions assembly to consume the firehose.
public sealed record AuditEventNotification
{
    // Identifier the host stamped on this event. Stable for the lifetime of
    // the row in the outbox / on the bus, so consumers can dedupe replays.
    public required Guid EventId { get; init; }

    // Dotted event identifier (e.g. "iam.user.viewed", "records.record.created").
    public required string EventType { get; init; }

    // Full Dapr topic name the envelope was enqueued onto (e.g. "iam.events").
    public required string TopicName { get; init; }

    // Short tag identifying the resource kind affected (e.g. "user", "record").
    public required string ResourceKind { get; init; }

    // Serialized envelope as it was handed to the outbox — contains everything
    // the consumer needs, including the typed Resource/Details payloads. Plugins
    // that want richer access than the flat fields below can json-parse this.
    public required string EnvelopeJson { get; init; }

    public required Guid? ActorId { get; init; }
    public required string? ActorUserName { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string RequestId { get; init; }
    public required string? CorrelationId { get; init; }
    public required string IpAddress { get; init; }
    public required string UserAgent { get; init; }
    public required string SourceAppId { get; init; }
    public required string HttpMethod { get; init; }
    public required string RoutePath { get; init; }
    public required AuditAuthOutcomeDto AuthOutcome { get; init; }
    public required string? AuthDecisionReason { get; init; }
}

public enum AuditAuthOutcomeDto
{
    // The request was permitted.
    Allowed = 0,
    // The request was rejected by the authorization layer.
    Denied = 1,
    // The request never presented a valid identity.
    Anonymous = 2,
}
