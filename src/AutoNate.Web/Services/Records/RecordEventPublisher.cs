using System.Net.Http.Headers;
using System.Text.Json;
using AutoNate.Web.Configuration;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Records;

public static class RecordEventTypes
{
    public const string Created = "record.created";
    public const string Updated = "record.updated";
    public const string Deleted = "record.deleted";
    public const string StatusChanged = "record.status.changed";
}

public sealed record class RecordEventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    Guid RecordId,
    string Key,
    Guid RecordTypeId,
    string Name,
    string? Status,
    string? PreviousStatus,
    IReadOnlyList<string> ChangedFields,
    IReadOnlyList<Guid> AssigneeIds,
    bool IsArchived,
    Guid ActorId,
    string SourceAppId);

public interface IRecordEventPublisher
{
    Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default);
}

// Posts record lifecycle events to a Dapr pub/sub topic (`record.events`).
// Wire format mirrors the Flowable extension: raw JSON payload (no CloudEvents
// envelope) so subscribers configured with `rawPayload=true` see exactly what
// we serialize here. Failure to publish is logged but does not fail the
// originating store operation — durability is best-effort, like the workflow
// telemetry topic.
public sealed class DaprRecordEventPublisher(
    IHttpClientFactory httpClientFactory,
    IOptions<DaprOptions> daprOptions,
    ILogger<DaprRecordEventPublisher> logger) : IRecordEventPublisher
{
    // Subject prefix shared by every record event topic; the JetStream stream
    // for records covers `record.>` so any future record.* topic is included.
    public const string TopicRoot = "record";
    public const string TopicName = "record.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DaprOptions _daprOptions = daprOptions.Value;

    public async Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            logger.LogWarning(
                "Skipping record event {EventType} for record {RecordId}: Dapr HTTP endpoint is not configured.",
                envelope.EventType, envelope.RecordId);
            return;
        }

        if (string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            logger.LogWarning(
                "Skipping record event {EventType} for record {RecordId}: Dapr PubSubName is not configured.",
                envelope.EventType, envelope.RecordId);
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
                    "Dapr publish returned HTTP {StatusCode} for record event {EventType} ({RecordId}).",
                    (int)response.StatusCode, envelope.EventType, envelope.RecordId);
            }
            else
            {
                logger.LogInformation(
                    "Published record event {EventType} for record {RecordId} ({Key}) to topic {Topic}.",
                    envelope.EventType, envelope.RecordId, envelope.Key, TopicName);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to publish record event {EventType} for {RecordId}.",
                envelope.EventType, envelope.RecordId);
        }
    }
}

// Used by tests and any deployment that wants record events disabled.
public sealed class NoopRecordEventPublisher : IRecordEventPublisher
{
    public Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
