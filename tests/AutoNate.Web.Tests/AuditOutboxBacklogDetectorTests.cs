using System.Text.Json;
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
public sealed class AuditOutboxBacklogDetectorTests
{
    [Fact]
    public async Task Empty_outbox_does_not_open_an_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var detector = CreateDetector(db, new AuditOutboxBacklogDetectorOptions());

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Recent_undispatched_rows_do_not_count_as_backlog()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        // Defaults: StaleAfter = 5 min. A just-inserted row should NOT trip
        // the detector even if it's undispatched.
        await SeedOutboxAsync(db, count: 100, ageInMinutes: 0);
        var detector = CreateDetector(db, new AuditOutboxBacklogDetectorOptions());

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Stale_backlog_above_warning_threshold_opens_warning_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await SeedOutboxAsync(db, count: 60, ageInMinutes: 10);
        var detector = CreateDetector(db, new AuditOutboxBacklogDetectorOptions
        {
            WarningAtCount = 50,
            ErrorAtCount = 500,
            CriticalAtCount = 5_000
        });

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueSeverities.Warning, issue.Severity);
        Assert.Equal("audit_outbox:backlog", issue.Fingerprint);
        Assert.Contains("60 undispatched", issue.Title);
        // JSONB normalizes whitespace on storage, so do a structured check
        // rather than substring match.
        using var facts = JsonDocument.Parse(issue.FactsJson);
        Assert.Equal(60, facts.RootElement.GetProperty("backlogCount").GetInt32());
    }

    [Fact]
    public async Task Severity_escalates_with_count()
    {
        var opts = new AuditOutboxBacklogDetectorOptions
        {
            WarningAtCount = 10,
            ErrorAtCount = 100,
            CriticalAtCount = 1_000
        };
        Assert.Equal(SystemIssueSeverities.Info, AuditOutboxBacklogDetector.ClassifySeverity(5, opts));
        Assert.Equal(SystemIssueSeverities.Warning, AuditOutboxBacklogDetector.ClassifySeverity(50, opts));
        Assert.Equal(SystemIssueSeverities.Error, AuditOutboxBacklogDetector.ClassifySeverity(500, opts));
        Assert.Equal(SystemIssueSeverities.Critical, AuditOutboxBacklogDetector.ClassifySeverity(5_000, opts));
    }

    [Fact]
    public async Task Drained_backlog_resolves_the_open_issue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await SeedOutboxAsync(db, count: 60, ageInMinutes: 10);
        var detector = CreateDetector(db, new AuditOutboxBacklogDetectorOptions
        {
            WarningAtCount = 50
        });

        await detector.RunOnceAsync(CancellationToken.None);
        // Mark every undispatched row as dispatched.
        await using (var seed = db.CreateDbContext())
        {
            await seed.AuditOutbox
                .Where(r => r.DispatchedAtUtc == null)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.DispatchedAtUtc, DateTime.UtcNow));
        }
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
        Assert.Equal(SystemIssueResolutionKinds.NoLongerPresent, issue.ResolutionKind);
    }

    [Fact]
    public async Task Dead_lettered_rows_do_not_count_as_backlog()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        // 100 rows past MaxAttempts — these belong to AuditOutboxDeadLetterDetector,
        // not the backlog detector. The backlog detector should ignore them.
        var auditOutboxOpts = new AuditOutboxOptions { MaxAttempts = 50 };
        await SeedOutboxAsync(db, count: 100, ageInMinutes: 60,
            attemptCount: auditOutboxOpts.MaxAttempts);
        var detector = CreateDetector(db,
            new AuditOutboxBacklogDetectorOptions { WarningAtCount = 1 },
            auditOutboxOpts);

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        Assert.Empty(await read.SystemIssues.AsNoTracking().ToListAsync());
    }

    private static AuditOutboxBacklogDetector CreateDetector(
        PostgresTestDatabase db,
        AuditOutboxBacklogDetectorOptions backlogOptions,
        AuditOutboxOptions? auditOutboxOptions = null)
    {
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        return new AuditOutboxBacklogDetector(
            dbContextFactory: db.CreateDbContextFactory(),
            recorder: store,
            backlogOptions: Options.Create(backlogOptions),
            auditOutboxOptions: Options.Create(auditOutboxOptions ?? new AuditOutboxOptions()),
            systemIssueOptions: Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            logger: NullLogger<AuditOutboxBacklogDetector>.Instance);
    }

    private static async Task SeedOutboxAsync(
        PostgresTestDatabase db,
        int count,
        int ageInMinutes,
        int attemptCount = 0)
    {
        await using var seed = db.CreateDbContext();
        var now = DateTime.UtcNow;
        var createdAt = now.AddMinutes(-ageInMinutes);
        for (var i = 0; i < count; i++)
        {
            seed.AuditOutbox.Add(new AuditOutboxEntry
            {
                Topic = "test.topic",
                EventType = "test.event",
                PayloadJson = "{}",
                CreatedAtUtc = createdAt,
                NextAttemptAfterUtc = createdAt,
                AttemptCount = attemptCount
            });
        }
        await seed.SaveChangesAsync();
    }
}
