using System.Text.Json;
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

        try
        {
            var payloadJson = JsonSerializer.Serialize(envelope, SerializerOptions);
            await outbox.EnqueueAsync(topicName, eventType, payloadJson, cancellationToken);
            logger.LogInformation(
                "Enqueued audit event {EventType} on {Topic}.", eventType, topicName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to enqueue audit event {EventType} on {Topic}.", eventType, topicName);
            AuditEventPublishMetrics.RecordFailure(topicName, ex.GetType().Name);
        }
    }
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
