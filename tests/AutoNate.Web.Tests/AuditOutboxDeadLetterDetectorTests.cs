using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AuditOutboxDeadLetterDetectorTests
{
    [Fact]
    public async Task No_dead_letters_records_no_issues()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        // Seed pending rows that haven't reached MaxAttempts — those are
        // the backlog detector's job, not this one's.
        await SeedRowAsync(db, attemptCount: 3, dispatched: false);
        var detector = CreateDetector(db);

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Each_dead_lettered_row_opens_a_per_row_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var auditOpts = new AuditOutboxOptions { MaxAttempts = 50 };
        var rowA = await SeedRowAsync(db, attemptCount: auditOpts.MaxAttempts,
            topic: "record.events", eventType: "record.created", lastError: "HTTP 500");
        var rowB = await SeedRowAsync(db, attemptCount: auditOpts.MaxAttempts + 5,
            topic: "auth.events", eventType: "auth.login.failed", lastError: "nats: timeout");
        // A normal row that should NOT be flagged.
        await SeedRowAsync(db, attemptCount: 1, dispatched: false);
        var detector = CreateDetector(db, auditOpts);

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issues = await read.SystemIssues.AsNoTracking().OrderBy(i => i.Fingerprint).ToListAsync();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Fingerprint == $"audit_outbox:dead_letter:{rowA.Id}");
        Assert.Contains(issues, i => i.Fingerprint == $"audit_outbox:dead_letter:{rowB.Id}");
        var aIssue = issues.First(i => i.Fingerprint == $"audit_outbox:dead_letter:{rowA.Id}");
        Assert.Equal(SystemIssueSeverities.Error, aIssue.Severity);
        Assert.Equal("audit_outbox", aIssue.RelatedEntityKind);
        Assert.Equal(rowA.Id.ToString(), aIssue.RelatedEntityId);
        Assert.Contains("record.events/record.created", aIssue.Title);
        Assert.Contains("HTTP 500", aIssue.Summary);
    }

    [Fact]
    public async Task Dispatched_rows_are_ignored_even_if_they_hit_max_attempts()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        // A row that eventually dispatched on the Nth attempt — the
        // dispatcher writes DispatchedAtUtc and stops retrying. The
        // dead-letter detector should not flag it.
        await SeedRowAsync(db,
            attemptCount: 100,
            dispatched: true);
        var detector = CreateDetector(db, new AuditOutboxOptions { MaxAttempts = 50 });

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Repeated_ticks_dedup_to_the_same_row()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var auditOpts = new AuditOutboxOptions { MaxAttempts = 50 };
        var row = await SeedRowAsync(db, attemptCount: auditOpts.MaxAttempts);
        var detector = CreateDetector(db, auditOpts);

        await detector.RunOnceAsync(CancellationToken.None);
        await detector.RunOnceAsync(CancellationToken.None);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal($"audit_outbox:dead_letter:{row.Id}", issue.Fingerprint);
        Assert.Equal(3, issue.OccurrenceCount);
    }

    [Fact]
    public async Task BatchSize_caps_per_tick_processing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var auditOpts = new AuditOutboxOptions { MaxAttempts = 50 };
        for (var i = 0; i < 7; i++)
        {
            await SeedRowAsync(db, attemptCount: auditOpts.MaxAttempts);
        }
        var detector = CreateDetector(db, auditOpts,
            new AuditOutboxDeadLetterDetectorOptions { BatchSize = 3 });

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Equal(3, await read.SystemIssues.AsNoTracking().CountAsync());
    }

    private static AuditOutboxDeadLetterDetector CreateDetector(
        PostgresTestDatabase db,
        AuditOutboxOptions? auditOutboxOptions = null,
        AuditOutboxDeadLetterDetectorOptions? deadLetterOptions = null)
    {
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new AuditOutboxDeadLetterDetector(
            dbContextFactory: db.CreateDbContextFactory(),
            recorder: store,
            deadLetterOptions: Options.Create(deadLetterOptions ?? new AuditOutboxDeadLetterDetectorOptions()),
            auditOutboxOptions: Options.Create(auditOutboxOptions ?? new AuditOutboxOptions()),
            systemIssueOptions: Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            logger: NullLogger<AuditOutboxDeadLetterDetector>.Instance);
    }

    private static async Task<AuditOutboxEntry> SeedRowAsync(
        PostgresTestDatabase db,
        int attemptCount,
        string topic = "test.topic",
        string eventType = "test.event",
        string? lastError = null,
        bool dispatched = false)
    {
        await using var seed = db.CreateDbContext();
        var now = DateTime.UtcNow;
        var row = new AuditOutboxEntry
        {
            Topic = topic,
            EventType = eventType,
            PayloadJson = "{}",
            CreatedAtUtc = now.AddMinutes(-30),
            NextAttemptAfterUtc = now,
            AttemptCount = attemptCount,
            LastError = lastError,
            DispatchedAtUtc = dispatched ? now : null
        };
        seed.AuditOutbox.Add(row);
        await seed.SaveChangesAsync();
        return row;
    }
}
