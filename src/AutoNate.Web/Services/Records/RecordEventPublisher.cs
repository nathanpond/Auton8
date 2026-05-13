using System.Text.Json;
using AutoNate.Web.Services.Audit;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Services.Records;

public static class RecordEventTypes
{
    public const string Created = "record.created";
    public const string Updated = "record.updated";
    // "Deleted" is the soft-delete / archive transition. Naming kept for
    // backward compatibility with subscribers that already filter on it.
    public const string Deleted = "record.deleted";
    public const string Restored = "record.restored";
    // Hard-delete: the row + all cascaded children (edges, comments, history,
    // watches) are gone. Subscribers that maintained mirrors should drop their
    // copies on this event.
    public const string Purged = "record.purged";
    public const string StatusChanged = "record.status.changed";
    public const string AssigneesChanged = "record.assignees.changed";

    // View events (Phase 4). Published from the cross-cutting
    // IAuditEventPublisher rather than the typed IRecordEventPublisher —
    // they don't carry the rich record envelope; just resource refs +
    // summary metadata.
    public const string Viewed = "record.viewed";
    public const string ListViewed = "record.list.viewed";
    public const string Searched = "record.searched";
    public const string HistoryViewed = "record.history.viewed";
}

public static class RecordResourceKinds
{
    public const string Record = "record";
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
    string SourceAppId,
    // Phase 1 of the audit-events plan: every envelope carries the shared
    // AuditContext. Nullable while consumers migrate; populated automatically
    // by DaprRecordEventPublisher from IRequestContext when callers don't
    // pre-fill it.
    AuditContext? AuditContext = null);

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
    IRequestContext requestContext,
    IAuditEventOutbox outbox,
    ILogger<DaprRecordEventPublisher> logger) : IRecordEventPublisher
{
    // Subject prefix shared by every record event topic; the JetStream stream
    // for records covers `record.>` so any future record.* topic is included.
    public const string TopicRoot = "record";
    public const string TopicName = "record.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        // Stamp the audit context unless the caller has already supplied one.
        // ActorId from the existing call signature wins over what's in the
        // claims, so background-service callers that pass a system-actor
        // sentinel keep that identity.
        var enriched = envelope.AuditContext is null
            ? envelope with
            {
                AuditContext = requestContext.BuildAuditContext(
                    actorIdOverride: envelope.ActorId,
                    occurredAtUtc: envelope.OccurredAtUtc,
                    sourceAppId: envelope.SourceAppId)
            }
            : envelope;

        try
        {
            var payloadJson = JsonSerializer.Serialize(enriched, SerializerOptions);
            await outbox.EnqueueAsync(TopicName, enriched.EventType, payloadJson, cancellationToken);
            logger.LogInformation(
                "Enqueued record event {EventType} for record {RecordId} ({Key}) to topic {Topic}.",
                enriched.EventType, enriched.RecordId, enriched.Key, TopicName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to enqueue record event {EventType} for {RecordId}.",
                enriched.EventType, enriched.RecordId);
            AuditEventPublishMetrics.RecordFailure(TopicName, ex.GetType().Name);
        }
    }
}

// Used by tests and any deployment that wants record events disabled.
public sealed class NoopRecordEventPublisher : IRecordEventPublisher
{
    public Task PublishAsync(RecordEventEnvelope envelope, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
