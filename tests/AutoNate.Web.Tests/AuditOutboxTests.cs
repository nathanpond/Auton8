using System.Net;
using AutoNate.Web.Configuration;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AuditOutboxTests
{
    [Fact]
    public async Task Enqueue_writes_a_pending_outbox_row()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var outbox = new EfCoreAuditEventOutbox(
            db.CreateDbContextFactory(),
            NullLogger<EfCoreAuditEventOutbox>.Instance);

        await outbox.EnqueueAsync("record.events", "record.created", "{\"hello\":1}");

        await using var read = db.CreateDbContext();
        var row = Assert.Single(await read.AuditOutbox.AsNoTracking().ToListAsync());
        Assert.Equal("record.events", row.Topic);
        Assert.Equal("record.created", row.EventType);
        Assert.Equal("{\"hello\":1}", row.PayloadJson);
        Assert.Null(row.DispatchedAtUtc);
        Assert.Equal(0, row.AttemptCount);
    }

    [Fact]
    public async Task Dispatcher_publishes_pending_rows_and_marks_them_dispatched()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var stub = new StubHttpMessageHandler()
            .WhenStatus(HttpMethod.Post, "/v1.0/publish/", HttpStatusCode.NoContent);

        // Seed one pending row.
        await using (var seed = db.CreateDbContext())
        {
            seed.AuditOutbox.Add(new AuditOutboxEntry
            {
                Topic = "record.events",
                EventType = "record.viewed",
                PayloadJson = "{\"eventId\":\"x\"}",
                CreatedAtUtc = DateTime.UtcNow,
                NextAttemptAfterUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            await seed.SaveChangesAsync();
        }

        var dispatcher = new AuditOutboxDispatcher(
            db.CreateDbContextFactory(),
            new SingleHandlerHttpClientFactory(stub),
            Options.Create(new DaprOptions
            {
                HttpEndpoint = "http://localhost:65000",
                PubSubName = "pubsub"
            }),
            Options.Create(new AuditOutboxOptions { Enabled = true }),
            NullLogger<AuditOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(1, dispatched);
        var captured = Assert.Single(stub.Requests);
        Assert.Contains("/v1.0/publish/pubsub/record.events", captured.Url);

        await using var read = db.CreateDbContext();
        var row = Assert.Single(await read.AuditOutbox.AsNoTracking().ToListAsync());
        Assert.NotNull(row.DispatchedAtUtc);
        Assert.Equal(1, row.AttemptCount);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task Dispatcher_failure_increments_attempt_count_and_backs_off()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        // Stub returns 500 — dispatcher should mark the row as failed and
        // schedule a retry in the future.
        var stub = new StubHttpMessageHandler()
            .WhenStatus(HttpMethod.Post, "/v1.0/publish/", HttpStatusCode.InternalServerError);

        await using (var seed = db.CreateDbContext())
        {
            seed.AuditOutbox.Add(new AuditOutboxEntry
            {
                Topic = "record.events",
                EventType = "record.viewed",
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                NextAttemptAfterUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            await seed.SaveChangesAsync();
        }

        var dispatcher = new AuditOutboxDispatcher(
            db.CreateDbContextFactory(),
            new SingleHandlerHttpClientFactory(stub),
            Options.Create(new DaprOptions
            {
                HttpEndpoint = "http://localhost:65000",
                PubSubName = "pubsub"
            }),
            Options.Create(new AuditOutboxOptions
            {
                Enabled = true,
                BaseBackoff = TimeSpan.FromSeconds(5)
            }),
            NullLogger<AuditOutboxDispatcher>.Instance);

        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var row = Assert.Single(await read.AuditOutbox.AsNoTracking().ToListAsync());
        Assert.Null(row.DispatchedAtUtc);
        Assert.Equal(1, row.AttemptCount);
        Assert.True(row.NextAttemptAfterUtc > DateTime.UtcNow.AddSeconds(2),
            $"expected backoff, but next_attempt_after_utc={row.NextAttemptAfterUtc:O}");
    }

    [Fact]
    public async Task Dispatcher_skips_rows_whose_backoff_has_not_expired()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var stub = new StubHttpMessageHandler()
            .WhenStatus(HttpMethod.Post, "/v1.0/publish/", HttpStatusCode.NoContent);

        await using (var seed = db.CreateDbContext())
        {
            seed.AuditOutbox.Add(new AuditOutboxEntry
            {
                Topic = "record.events",
                EventType = "record.viewed",
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                // Far future — dispatcher should not pick this up.
                NextAttemptAfterUtc = DateTime.UtcNow.AddHours(1)
            });
            await seed.SaveChangesAsync();
        }

        var dispatcher = new AuditOutboxDispatcher(
            db.CreateDbContextFactory(),
            new SingleHandlerHttpClientFactory(stub),
            Options.Create(new DaprOptions
            {
                HttpEndpoint = "http://localhost:65000",
                PubSubName = "pubsub"
            }),
            Options.Create(new AuditOutboxOptions { Enabled = true }),
            NullLogger<AuditOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(0, dispatched);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Dispatcher_stops_retrying_after_MaxAttempts()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var stub = new StubHttpMessageHandler()
            .WhenStatus(HttpMethod.Post, "/v1.0/publish/", HttpStatusCode.NoContent);

        await using (var seed = db.CreateDbContext())
        {
            seed.AuditOutbox.Add(new AuditOutboxEntry
            {
                Topic = "record.events",
                EventType = "record.viewed",
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                AttemptCount = 50,                  // already at the cap
                NextAttemptAfterUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            await seed.SaveChangesAsync();
        }

        var dispatcher = new AuditOutboxDispatcher(
            db.CreateDbContextFactory(),
            new SingleHandlerHttpClientFactory(stub),
            Options.Create(new DaprOptions
            {
                HttpEndpoint = "http://localhost:65000",
                PubSubName = "pubsub"
            }),
            Options.Create(new AuditOutboxOptions
            {
                Enabled = true,
                MaxAttempts = 50
            }),
            NullLogger<AuditOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(0, dispatched);
        Assert.Empty(stub.Requests);
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
