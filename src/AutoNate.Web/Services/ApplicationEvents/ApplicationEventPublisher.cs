using System.Net.Http.Headers;
using System.Text.Json;
using AutoNate.Web.Configuration;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.ApplicationEvents;

public static class ApplicationEventTypes
{
    public const string PluginUploaded = "plugin.uploaded";
    public const string PluginEnabled = "plugin.enabled";
    public const string PluginDisabled = "plugin.disabled";
    public const string PluginDeleted = "plugin.deleted";
    public const string PluginEnableFailed = "plugin.enable_failed";
}

public sealed record class ApplicationEventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    object Payload,
    string SourceAppId);

public interface IApplicationEventPublisher
{
    Task PublishAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default);
}

// Posts in-app lifecycle events (plugin uploaded/enabled/etc.) to a Dapr
// pub/sub topic. Mirrors DaprRecordEventPublisher: raw JSON, fire-and-forget,
// failures logged but don't fail the originating operation.
public sealed class DaprApplicationEventPublisher(
    IHttpClientFactory httpClientFactory,
    IOptions<DaprOptions> daprOptions,
    ILogger<DaprApplicationEventPublisher> logger) : IApplicationEventPublisher
{
    public const string TopicRoot = "application";
    public const string TopicName = "application.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DaprOptions _daprOptions = daprOptions.Value;

    public async Task PublishAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            logger.LogWarning(
                "Skipping application event {EventType} ({EventId}): Dapr HTTP endpoint is not configured.",
                envelope.EventType, envelope.EventId);
            return;
        }

        if (string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            logger.LogWarning(
                "Skipping application event {EventType} ({EventId}): Dapr PubSubName is not configured.",
                envelope.EventType, envelope.EventId);
            return;
        }

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
                    "Dapr publish returned HTTP {StatusCode} for application event {EventType} ({EventId}).",
                    (int)response.StatusCode, envelope.EventType, envelope.EventId);
            }
            else
            {
                logger.LogInformation(
                    "Published application event {EventType} ({EventId}) to topic {Topic}.",
                    envelope.EventType, envelope.EventId, TopicName);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to publish application event {EventType} ({EventId}).",
                envelope.EventType, envelope.EventId);
        }
    }
}

public sealed class NoopApplicationEventPublisher : IApplicationEventPublisher
{
    public Task PublishAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
