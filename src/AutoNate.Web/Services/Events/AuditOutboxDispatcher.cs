using System.Net.Http.Headers;
using System.Text;
using AutoNate.Web.Configuration;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Events;

public sealed class AuditOutboxOptions
{
    public const string SectionName = "AuditOutbox";

    // Outbox is on by default in Phase 5 — flip to false to revert to direct
    // fire-and-forget publishing (the pre-Phase-5 behavior).
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 100;

    // Backoff is exponential, capped: attempt N waits min(BaseBackoff * 2^N, MaxBackoff).
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

    // After this many failed attempts, the dispatcher stops retrying and
    // leaves the row for an operator to inspect. The metric counter still
    // ticks every time we skip such a row.
    public int MaxAttempts { get; set; } = 50;
}

// Phase 5 of the audit-events plan. Polls the audit_outbox table for
// undispatched rows whose backoff has expired, claims them with FOR UPDATE
// SKIP LOCKED so multiple instances don't double-publish, and posts each to
// the Dapr pub/sub gateway. Successful rows are marked dispatched; failed
// rows get an exponentially-backed-off NextAttemptAfterUtc and an error
// message stamped on. Rows that exceed MaxAttempts are abandoned (still
// readable in the table for triage; not retried).
public sealed class AuditOutboxDispatcher(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<DaprOptions> daprOptions,
    IOptions<AuditOutboxOptions> outboxOptions,
    ILogger<AuditOutboxDispatcher> logger) : BackgroundService
{
    // Named so the timeout below is attached to *this* caller rather than to
    // the shared unnamed client every other consumer also resolves (archived-71).
    public const string HttpClientName = "audit-outbox";

    private readonly DaprOptions _daprOptions = daprOptions.Value;
    private readonly AuditOutboxOptions _outboxOptions = outboxOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_outboxOptions.Enabled)
        {
            logger.LogInformation(
                "Audit outbox dispatcher disabled via {Section}:Enabled.",
                AuditOutboxOptions.SectionName);
            return;
        }

        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out _)
            || string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            logger.LogWarning(
                "Audit outbox dispatcher idling: Dapr HttpEndpoint or PubSubName not configured. Outbox rows will accumulate until the configuration is supplied.");
        }

        var poll = _outboxOptions.PollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                // Backoff to PollInterval only when no work was done. When the
                // outbox is busy, drain immediately.
                if (dispatched == 0)
                {
                    await Task.Delay(poll, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Log and continue — the dispatcher must never die.
                logger.LogError(ex, "Audit outbox dispatcher tick failed; retrying after PollInterval.");
                try { await Task.Delay(poll, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // Public so tests can drive a single tick without spinning up the
    // BackgroundService loop. Returns the number of rows dispatched (success
    // or failure) in this pass.
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Claim a batch with FOR UPDATE SKIP LOCKED so concurrent dispatchers
        // (multiple instances) don't double-publish. The transaction holds
        // the locks until commit, so we keep this scope short — publish
        // happens inside, then the row is updated and the tx commits.
        var batch = await dbContext.AuditOutbox
            .FromSqlRaw(
                """
                SELECT * FROM audit_outbox
                WHERE dispatched_at_utc IS NULL
                  AND next_attempt_after_utc <= NOW()
                  AND attempt_count < {0}
                ORDER BY id
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                _outboxOptions.MaxAttempts,
                _outboxOptions.BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return 0;
        }

        foreach (var row in batch)
        {
            var success = await TryPublishAsync(row.Topic, row.PayloadJson, cancellationToken);
            row.AttemptCount += 1;
            if (success)
            {
                row.DispatchedAtUtc = DateTime.UtcNow;
                row.LastError = null;
                AuditEventPublishMetrics.RecordDispatched(row.Topic);
            }
            else
            {
                var backoff = ComputeBackoff(row.AttemptCount);
                row.NextAttemptAfterUtc = DateTime.UtcNow + backoff;
                AuditEventPublishMetrics.RecordDispatchFailure(row.Topic, "publish_failed");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return batch.Count;
    }

    private async Task<bool> TryPublishAsync(string topic, string payloadJson, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            return false;
        }

        var pubsub = Uri.EscapeDataString(_daprOptions.PubSubName);
        var topicEscaped = Uri.EscapeDataString(topic);
        var publishUri = new Uri(endpoint, $"/v1.0/publish/{pubsub}/{topicEscaped}?metadata.rawPayload=true");

        try
        {
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(payloadJson));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.PostAsync(publishUri, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            logger.LogError(
                "Outbox dispatch returned HTTP {StatusCode} for topic {Topic}.",
                (int)response.StatusCode, topic);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Outbox dispatch failed for topic {Topic}.", topic);
            return false;
        }
    }

    private TimeSpan ComputeBackoff(int attemptCount)
    {
        // Exponential, capped. attempt 1 → BaseBackoff, attempt 2 → 2× base, etc.
        var multiplier = 1L << Math.Min(attemptCount - 1, 30);
        var ticks = _outboxOptions.BaseBackoff.Ticks * multiplier;
        if (ticks > _outboxOptions.MaxBackoff.Ticks || ticks < 0)
        {
            return _outboxOptions.MaxBackoff;
        }
        return TimeSpan.FromTicks(ticks);
    }
}
