using System.Net.Http.Headers;
using System.Text.Json;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models.Notifications;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Notifications;

public static class NotificationEventTypes
{
    public const string Created = "notification.created";
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
    string SourceAppId);

public interface INotificationEventPublisher
{
    Task PublishAsync(Notification notification, CancellationToken cancellationToken = default);
}

// Posts notification.created events to a Dapr pub/sub topic. Mirrors
// DaprRecordEventPublisher: raw JSON payload (no CloudEvents envelope) so
// subscribers configured with rawPayload=true see exactly what we serialize.
// Failure to publish is logged but does not fail the originating store
// operation — the persisted row in the notifications table is the source of
// truth, and the SPA picks it up on next REST refresh.
public sealed class DaprNotificationEventPublisher(
    IHttpClientFactory httpClientFactory,
    IOptions<DaprOptions> daprOptions,
    ILogger<DaprNotificationEventPublisher> logger) : INotificationEventPublisher
{
    public const string TopicRoot = "notification";
    public const string TopicName = "notification.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DaprOptions _daprOptions = daprOptions.Value;

    private string SourceAppId => string.IsNullOrWhiteSpace(_daprOptions.AppId)
        ? "autonate.web"
        : _daprOptions.AppId;

    public async Task PublishAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            logger.LogWarning(
                "Skipping notification event {NotificationId}: Dapr HTTP endpoint is not configured.",
                notification.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            logger.LogWarning(
                "Skipping notification event {NotificationId}: Dapr PubSubName is not configured.",
                notification.Id);
            return;
        }

        var envelope = new NotificationEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: NotificationEventTypes.Created,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            NotificationId: notification.Id,
            UserId: notification.UserId,
            Kind: notification.Kind,
            Title: notification.Title,
            Body: notification.Body,
            RelatedEntityKind: notification.RelatedEntityKind,
            RelatedEntityId: notification.RelatedEntityId,
            LinkPath: notification.LinkPath,
            SourceAppId: SourceAppId);

        var pubsub = Uri.EscapeDataString(_daprOptions.PubSubName);
        var topic = Uri.EscapeDataString(TopicName);
        var publishUri = new Uri(endpoint, $"/v1.0/publish/{pubsub}/{topic}?metadata.rawPayload=true");

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var httpClient = httpClientFactory.CreateClient();
            using var response = await httpClient.PostAsync(publishUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Dapr publish returned HTTP {StatusCode} for notification {NotificationId}.",
                    (int)response.StatusCode, notification.Id);
            }
            else
            {
                logger.LogInformation(
                    "Published notification.created for {NotificationId} (user {UserId}) to topic {Topic}.",
                    notification.Id, notification.UserId, TopicName);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to publish notification event for {NotificationId}.", notification.Id);
        }
    }
}

public sealed class NoopNotificationEventPublisher : INotificationEventPublisher
{
    public Task PublishAsync(Notification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
