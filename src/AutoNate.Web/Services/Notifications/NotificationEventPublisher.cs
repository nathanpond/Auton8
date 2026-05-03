using System.Text.Json;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.Audit;
using AutoNate.Web.Services.Events;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Notifications;

public static class NotificationEventTypes
{
    public const string Created = "notification.created";
    public const string Removed = "notification.removed";
    public const string Read = "notification.read";
    public const string AllRead = "notification.all.read";

    // View events (Phase 4)
    public const string ListViewed = "notification.list.viewed";
    // Per-user 60-second coalesce window — see ViewEventCoalescer.
    public const string UnreadCountViewed = "notification.unread.count.viewed";
}

public static class NotificationResourceKinds
{
    public const string Notification = "notification";
    public const string NotificationCollection = "notification.collection";
}

public sealed record class NotificationEventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    Guid NotificationId,
    Guid UserId,
    string Kind,
    string Title,
    string Body,
    string? RelatedEntityKind,
    string? RelatedEntityId,
    string? LinkPath,
    string SourceAppId,
    AuditContext? AuditContext = null);

public interface INotificationEventPublisher
{
    Task PublishAsync(Notification notification, CancellationToken cancellationToken = default);

    Task PublishRemovedAsync(Notification notification, CancellationToken cancellationToken = default);
}

// Posts notification.created events to a Dapr pub/sub topic. Mirrors
// DaprRecordEventPublisher: raw JSON payload (no CloudEvents envelope) so
// subscribers configured with rawPayload=true see exactly what we serialize.
// Failure to publish is logged but does not fail the originating store
// operation — the persisted row in the notifications table is the source of
// truth, and the SPA picks it up on next REST refresh.
public sealed class DaprNotificationEventPublisher(
    IOptions<DaprOptions> daprOptions,
    IRequestContext requestContext,
    IAuditEventOutbox outbox,
    ILogger<DaprNotificationEventPublisher> logger) : INotificationEventPublisher
{
    public const string TopicRoot = "notification";
    public const string TopicName = "notification.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly DaprOptions _daprOptions = daprOptions.Value;

    private string SourceAppId => string.IsNullOrWhiteSpace(_daprOptions.AppId)
        ? "autonate.web"
        : _daprOptions.AppId;

    public Task PublishAsync(Notification notification, CancellationToken cancellationToken = default) =>
        PublishEnvelopeAsync(notification, NotificationEventTypes.Created, cancellationToken);

    public Task PublishRemovedAsync(Notification notification, CancellationToken cancellationToken = default) =>
        PublishEnvelopeAsync(notification, NotificationEventTypes.Removed, cancellationToken);

    private async Task PublishEnvelopeAsync(
        Notification notification,
        string eventType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var occurredAt = DateTimeOffset.UtcNow;
        var envelope = new NotificationEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            OccurredAtUtc: occurredAt,
            NotificationId: notification.Id,
            UserId: notification.UserId,
            Kind: notification.Kind,
            Title: notification.Title,
            Body: notification.Body,
            RelatedEntityKind: notification.RelatedEntityKind,
            RelatedEntityId: notification.RelatedEntityId,
            LinkPath: notification.LinkPath,
            SourceAppId: SourceAppId,
            AuditContext: requestContext.BuildAuditContext(
                occurredAtUtc: occurredAt,
                sourceAppId: SourceAppId));

        try
        {
            var payloadJson = JsonSerializer.Serialize(envelope, SerializerOptions);
            await outbox.EnqueueAsync(TopicName, envelope.EventType, payloadJson, cancellationToken);
            logger.LogInformation(
                "Enqueued {EventType} for {NotificationId} (user {UserId}) to topic {Topic}.",
                eventType, notification.Id, notification.UserId, TopicName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to enqueue notification event {EventType} for {NotificationId}.",
                eventType, notification.Id);
            AuditEventPublishMetrics.RecordFailure(TopicName, ex.GetType().Name);
        }
    }
}

public sealed class NoopNotificationEventPublisher : INotificationEventPublisher
{
    public Task PublishAsync(Notification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishRemovedAsync(Notification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
