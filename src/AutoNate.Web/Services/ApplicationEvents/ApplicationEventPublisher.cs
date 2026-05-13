using System.Text.Json;
using AutoNate.Web.Services.Audit;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Services.ApplicationEvents;

public static class ApplicationEventTypes
{
    public const string PluginUploaded = "plugin.uploaded";
    public const string PluginUpdated = "plugin.updated";
    public const string PluginEnabled = "plugin.enabled";
    public const string PluginDisabled = "plugin.disabled";
    public const string PluginDeleted = "plugin.deleted";
    public const string PluginEnableFailed = "plugin.enable_failed";

    // View events (Phase 4)
    public const string PluginListViewed = "plugin.list.viewed";
    public const string PluginViewed = "plugin.viewed";

    // Per-plugin admin endpoints (settings KV + generic data view). Both view
    // and update emit so an audit consumer can answer "who poked the
    // Auditor's settings yesterday?" or "who opened the AuditLog page?".
    public const string PluginSettingsViewed = "plugin.settings.viewed";
    public const string PluginSettingsUpdated = "plugin.settings.updated";
    public const string PluginDataViewed = "plugin.data.viewed";
}

public static class ApplicationResourceKinds
{
    public const string Plugin = "plugin";
    public const string PluginSettings = "plugin.settings";
    public const string PluginData = "plugin.data";
}

public sealed record class ApplicationEventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    object Payload,
    string SourceAppId,
    AuditContext? AuditContext = null);

public interface IApplicationEventPublisher
{
    Task PublishAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default);
}

// Posts in-app lifecycle events (plugin uploaded/enabled/etc.) to a Dapr
// pub/sub topic. Mirrors DaprRecordEventPublisher: raw JSON, fire-and-forget,
// failures logged but don't fail the originating operation.
public sealed class DaprApplicationEventPublisher(
    IRequestContext requestContext,
    IAuditEventOutbox outbox,
    ILogger<DaprApplicationEventPublisher> logger) : IApplicationEventPublisher
{
    public const string TopicRoot = "application";
    public const string TopicName = "application.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task PublishAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var enriched = envelope.AuditContext is null
            ? envelope with
            {
                AuditContext = requestContext.BuildAuditContext(
                    actorIdOverride: envelope.ActorUserId,
                    occurredAtUtc: envelope.OccurredAtUtc,
                    sourceAppId: envelope.SourceAppId)
            }
            : envelope;

        try
        {
            var payloadJson = JsonSerializer.Serialize(enriched, SerializerOptions);
            await outbox.EnqueueAsync(TopicName, enriched.EventType, payloadJson, cancellationToken);
            logger.LogInformation(
                "Enqueued application event {EventType} ({EventId}) to topic {Topic}.",
                enriched.EventType, enriched.EventId, TopicName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to enqueue application event {EventType} ({EventId}).",
                enriched.EventType, enriched.EventId);
            AuditEventPublishMetrics.RecordFailure(TopicName, ex.GetType().Name);
        }
    }
}

public sealed class NoopApplicationEventPublisher : IApplicationEventPublisher
{
    public Task PublishAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
