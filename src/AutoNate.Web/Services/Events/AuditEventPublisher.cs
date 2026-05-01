using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Audit;

namespace AutoNate.Web.Services.Events;

// Cross-cutting event envelope used by IAuditEventPublisher. Domains with
// rich, well-typed payloads (records, plugins, notifications) keep their
// bespoke envelopes; this is the fallback shape for view events and thin
// mutations that don't merit a domain-specific envelope/publisher.
public sealed record AuditEventEnvelope(
    Guid EventId,
    string EventType,
    string ResourceKind,
    object? Resource,
    object? Details,
    AuditContext AuditContext);

public interface IAuditEventPublisher
{
    // Publishes a single audit event to the bus.
    //   topicName       — full Dapr topic name, e.g. "iam.events".
    //   eventType       — dotted event identifier, e.g. "iam.user.viewed".
    //   resourceKind    — short tag identifying the kind of thing affected,
    //                     e.g. "user", "group", "record-type".
    //   resource        — small typed payload identifying the resource (id +
    //                     human-readable key/name). Pass null for events that
    //                     don't refer to a single resource.
    //   details         — event-specific extras (filter hash, page number,
    //                     deny reason). Capped by the publisher to a JSON
    //                     size limit so a runaway payload can't bloat the
    //                     stream. Pass null when there are no extras.
    Task PublishAsync(
        string topicName,
        string eventType,
        string resourceKind,
        object? resource,
        object? details,
        CancellationToken cancellationToken = default);
}

// Phase 5 of the audit-events plan: this publisher now serializes the envelope
// and hands it to IAuditEventOutbox. The outbox impl (durable EfCore by default,
// or DirectPublish when AuditOutbox:Enabled=false) handles delivery to Dapr.
public sealed class DaprAuditEventPublisher(
    IRequestContext requestContext,
    IAuditEventOutbox outbox,
    IActionHub actionHub,
    ILogger<DaprAuditEventPublisher> logger) : IAuditEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task PublishAsync(
        string topicName,
        string eventType,
        string resourceKind,
        object? resource,
        object? details,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new ArgumentException("Topic name is required.", nameof(topicName));
        }
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required.", nameof(eventType));
        }

        var envelope = new AuditEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            ResourceKind: resourceKind,
            Resource: resource,
            Details: details,
            AuditContext: requestContext.BuildAuditContext());

        string payloadJson;
        try
        {
            payloadJson = JsonSerializer.Serialize(envelope, SerializerOptions);
            await outbox.EnqueueAsync(topicName, eventType, payloadJson, cancellationToken);
            logger.LogInformation(
                "Enqueued audit event {EventType} on {Topic}.", eventType, topicName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to enqueue audit event {EventType} on {Topic}.", eventType, topicName);
            AuditEventPublishMetrics.RecordFailure(topicName, ex.GetType().Name);
            return;
        }

        // Notify in-process plugin subscribers. ActionHub already isolates
        // throwing callbacks, so a misbehaving plugin can't break the request.
        if (actionHub.HasAction(HookPoints.AuditEventPublished))
        {
            var notification = BuildNotification(topicName, envelope, payloadJson);
            await actionHub.DoAsync(HookPoints.AuditEventPublished, cancellationToken, notification);
        }
    }

    private static AuditEventNotification BuildNotification(
        string topicName, AuditEventEnvelope envelope, string envelopeJson) => new()
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            TopicName = topicName,
            ResourceKind = envelope.ResourceKind,
            EnvelopeJson = envelopeJson,
            ActorId = envelope.AuditContext.ActorId,
            ActorUserName = envelope.AuditContext.ActorUserName,
            OccurredAtUtc = envelope.AuditContext.OccurredAtUtc,
            RequestId = envelope.AuditContext.RequestId,
            CorrelationId = envelope.AuditContext.CorrelationId,
            IpAddress = envelope.AuditContext.IpAddress,
            UserAgent = envelope.AuditContext.UserAgent,
            SourceAppId = envelope.AuditContext.SourceAppId,
            HttpMethod = envelope.AuditContext.HttpMethod,
            RoutePath = envelope.AuditContext.RoutePath,
            AuthOutcome = envelope.AuditContext.AuthOutcome switch
            {
                AuthOutcome.Allowed => AuditAuthOutcomeDto.Allowed,
                AuthOutcome.Denied => AuditAuthOutcomeDto.Denied,
                AuthOutcome.Anonymous => AuditAuthOutcomeDto.Anonymous,
                _ => AuditAuthOutcomeDto.Allowed,
            },
            AuthDecisionReason = envelope.AuditContext.AuthDecisionReason,
        };
}

public sealed class NoopAuditEventPublisher : IAuditEventPublisher
{
    public Task PublishAsync(
        string topicName,
        string eventType,
        string resourceKind,
        object? resource,
        object? details,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
