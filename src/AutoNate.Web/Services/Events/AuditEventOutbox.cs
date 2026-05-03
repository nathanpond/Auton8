using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Events;

// Phase 5 of the audit-events plan. Live publishers write to the outbox
// instead of POSTing to Dapr directly; AuditOutboxDispatcher reads the
// outbox and does the actual Dapr publish. This breaks the
// publisher-blocks-on-bus dependency: events survive Dapr/NATS hiccups
// without being lost. Note: the enqueue is NOT atomic with the upstream
// domain transaction today — the row is written from a fresh DbContext
// after the domain commit. Closing that gap (passing the domain DbContext
// into the publisher so the outbox row writes inside the same transaction)
// is a follow-up refactor; until then, an outbox-write failure between
// successful domain commit and successful enqueue can still drop an event,
// same as the pre-Phase-5 fire-and-forget path.
public interface IAuditEventOutbox
{
    Task EnqueueAsync(
        string topic,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default);
}

public sealed class EfCoreAuditEventOutbox(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<EfCoreAuditEventOutbox> logger) : IAuditEventOutbox
{
    // Cap the enqueue at a finite duration so a wedged DB can't hang the caller forever,
    // but don't tie the cap to the request's CancellationToken — see EnqueueAsync below.
    private static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(30);

    public async Task EnqueueAsync(
        string topic,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        // Deliberately ignore the caller's cancellationToken for the DB write.
        // Publishers call us AFTER the upstream domain transaction has committed,
        // so the audit row must be persisted even if the originating HTTP request
        // is being torn down (e.g. user navigated away). Honoring the request token
        // here causes Npgsql to abort SaveChangesAsync with OperationCanceledException
        // and silently drop the event.
        using var timeoutCts = new CancellationTokenSource(EnqueueTimeout);
        var writeToken = timeoutCts.Token;
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(writeToken);
            var now = DateTime.UtcNow;
            dbContext.AuditOutbox.Add(new AuditOutboxEntry
            {
                Topic = topic,
                EventType = eventType,
                PayloadJson = payloadJson,
                CreatedAtUtc = now,
                DispatchedAtUtc = null,
                AttemptCount = 0,
                LastError = null,
                NextAttemptAfterUtc = now
            });
            await dbContext.SaveChangesAsync(writeToken);
            AuditEventPublishMetrics.RecordEnqueue(topic);
        }
        catch (Exception ex)
        {
            // Do not throw — losing an audit event in transit is bad, but
            // crashing the user's request is worse. Log loudly so operators
            // see the breakage and the metric counter ticks up.
            logger.LogError(ex,
                "Failed to enqueue audit event {EventType} on {Topic} to outbox; event will be lost.",
                eventType, topic);
            AuditEventPublishMetrics.RecordFailure(topic, "outbox_enqueue_" + ex.GetType().Name);
        }
    }
}

// Test/dev fallback. When the outbox is disabled the publisher uses this and
// posts to Dapr directly (the pre-Phase-5 behavior).
public sealed class NoopAuditEventOutbox : IAuditEventOutbox
{
    public Task EnqueueAsync(
        string topic,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// Pre-Phase-5 fallback path. When AuditOutbox:Enabled is false, the publishers
// resolve this impl instead of EfCoreAuditEventOutbox, and every "enqueue"
// turns into an inline POST to the Dapr sidecar — the same fire-and-forget
// best-effort behavior the codebase had before Phase 5.
public sealed class DirectPublishAuditEventOutbox(
    IHttpClientFactory httpClientFactory,
    IOptions<Configuration.DaprOptions> daprOptions,
    ILogger<DirectPublishAuditEventOutbox> logger) : IAuditEventOutbox
{
    private readonly Configuration.DaprOptions _daprOptions = daprOptions.Value;

    public async Task EnqueueAsync(
        string topic,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            logger.LogError(
                "Skipping direct publish of {EventType} on {Topic}: Dapr HTTP endpoint is not configured.",
                eventType, topic);
            AuditEventPublishMetrics.RecordFailure(topic, "dapr_endpoint_missing");
            return;
        }
        if (string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            logger.LogError(
                "Skipping direct publish of {EventType} on {Topic}: Dapr PubSubName is not configured.",
                eventType, topic);
            AuditEventPublishMetrics.RecordFailure(topic, "dapr_pubsub_missing");
            return;
        }

        var pubsub = Uri.EscapeDataString(_daprOptions.PubSubName);
        var topicEscaped = Uri.EscapeDataString(topic);
        var publishUri = new Uri(endpoint, $"/v1.0/publish/{pubsub}/{topicEscaped}?metadata.rawPayload=true");

        try
        {
            using var content = new System.Net.Http.ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(payloadJson));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var httpClient = httpClientFactory.CreateClient();
            using var response = await httpClient.PostAsync(publishUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Direct publish returned HTTP {StatusCode} for {EventType} on {Topic}.",
                    (int)response.StatusCode, eventType, topic);
                AuditEventPublishMetrics.RecordFailure(topic, $"http_{(int)response.StatusCode}");
                return;
            }
            AuditEventPublishMetrics.RecordSuccess(topic);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex,
                "Direct publish failed for {EventType} on {Topic}.", eventType, topic);
            AuditEventPublishMetrics.RecordFailure(topic, ex.GetType().Name);
        }
    }
}
