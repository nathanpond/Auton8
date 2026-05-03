using AutoNate.Web.Configuration;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;
using AutoNate.Web.Services.SystemIssues.Remediators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using AuditOutboxEntry = AutoNate.Web.Persistence.Scaffolded.AuditOutboxEntry;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SystemIssueRemediationTests
{
    [Fact]
    public async Task Detector_opens_dead_letter_issue_with_remediation_due_so_dispatcher_picks_it_up()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var auditOpts = new AuditOutboxOptions { MaxAttempts = 50 };
        var deadRow = await SeedDeadLetterAsync(db, auditOpts.MaxAttempts, "record.events", "record.created", "HTTP 500");

        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        var detector = new AuditOutboxDeadLetterDetector(
            db.CreateDbContextFactory(), store,
            Options.Create(new AuditOutboxDeadLetterDetectorOptions()),
            Options.Create(auditOpts),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<AuditOutboxDeadLetterDetector>.Instance);

        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal($"audit_outbox:dead_letter:{deadRow.Id}", issue.Fingerprint);
        Assert.NotNull(issue.NextRemediationAfterUtc); // detector opted into auto-remediation
    }

    [Fact]
    public async Task Dispatcher_routes_dead_letter_issue_to_park_remediator_and_marks_auto_resolved()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var auditOpts = new AuditOutboxOptions { MaxAttempts = 50 };
        var deadRow = await SeedDeadLetterAsync(db, auditOpts.MaxAttempts, "record.events", "record.created", "HTTP 500");

        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        var detector = new AuditOutboxDeadLetterDetector(
            db.CreateDbContextFactory(), store,
            Options.Create(new AuditOutboxDeadLetterDetectorOptions()),
            Options.Create(auditOpts),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<AuditOutboxDeadLetterDetector>.Instance);
        await detector.RunOnceAsync(CancellationToken.None);

        var dispatcher = CreateDispatcher(db,
            new AuditOutboxDeadLetterParkRemediator(
                db.CreateDbContextFactory(),
                NullLogger<AuditOutboxDeadLetterParkRemediator>.Instance));

        var dispatched = await dispatcher.DispatchBatchAsync(CancellationToken.None);
        Assert.Equal(1, dispatched);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.AutoResolved, issue.State);
        Assert.Equal(SystemIssueResolutionKinds.AutoRemediated, issue.ResolutionKind);

        // The audit_outbox row is gone…
        Assert.Empty(await read.AuditOutbox.AsNoTracking().ToListAsync());

        // …and the row is parked with reason metadata.
        await using var conn = (NpgsqlConnection)read.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT original_outbox_id, topic, event_type, parked_reason FROM audit_outbox_dead_letters",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(deadRow.Id, reader.GetInt64(0));
        Assert.Equal("record.events", reader.GetString(1));
        Assert.Equal("record.created", reader.GetString(2));
        Assert.Contains("Parked by AuditOutboxDeadLetterParkRemediator", reader.GetString(3));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Failing_remediator_caps_attempts_and_leaves_issue_open()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        // Hand-craft a draft for the synthetic always-failing detector class.
        await store.RecordAsync(new SystemIssueDraft(
            DetectorId: AlwaysFailRemediator.DetectorIdValue,
            Category: SystemIssueCategories.Bus,
            Severity: SystemIssueSeverities.Error,
            Fingerprint: "synthetic:always-fail",
            Title: "stays open after 3 strikes",
            RemediationDueAtUtc: DateTime.UtcNow));

        var sysOpts = new SystemIssueOptions
        {
            MaxRemediationAttempts = 3,
            RemediationBaseBackoff = TimeSpan.Zero,   // no backoff so DispatchBatchAsync re-picks immediately
            RemediationMaxBackoff = TimeSpan.Zero
        };
        var dispatcher = CreateDispatcher(db, new AlwaysFailRemediator(), sysOpts);

        // Three ticks: the row stays in the eligible window because backoff
        // is zero. After the third the dispatcher caps at MaxAttempts and
        // clears next_remediation_after_utc.
        await dispatcher.DispatchBatchAsync(CancellationToken.None);
        await dispatcher.DispatchBatchAsync(CancellationToken.None);
        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(SystemIssueStates.Open, issue.State);
        Assert.Equal(3, issue.AutoRemediationAttemptCount);
        Assert.Null(issue.NextRemediationAfterUtc); // dispatcher gave up
        Assert.NotNull(issue.AutoRemediationLastError);
    }

    [Fact]
    public async Task Re_detection_does_not_reset_dispatcher_backoff()
    {
        // Verifies the per-row contract: detectors bumping occurrence_count
        // on existing issues must NOT reset next_remediation_after_utc, or
        // the dispatcher's exponential backoff would never make progress.
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        var first = await store.RecordAsync(new SystemIssueDraft(
            DetectorId: "test", Category: SystemIssueCategories.Bus, Severity: SystemIssueSeverities.Warning,
            Fingerprint: "test:backoff", Title: "first",
            RemediationDueAtUtc: DateTime.UtcNow.AddMinutes(-10)));

        // Simulate the dispatcher having pushed next_remediation_after_utc
        // out into the future after a failed attempt.
        var future = DateTime.UtcNow.AddHours(2);
        await using (var seed = db.CreateDbContext())
        {
            var row = await seed.SystemIssues.FirstAsync(i => i.Id == first.IssueId);
            row.NextRemediationAfterUtc = future;
            row.AutoRemediationAttemptCount = 1;
            await seed.SaveChangesAsync();
        }

        // A second detection (same fingerprint) tries to set RemediationDueAtUtc=now()
        // but the upsert path must NOT touch next_remediation_after_utc on update.
        await store.RecordAsync(new SystemIssueDraft(
            DetectorId: "test", Category: SystemIssueCategories.Bus, Severity: SystemIssueSeverities.Warning,
            Fingerprint: "test:backoff", Title: "first",
            RemediationDueAtUtc: DateTime.UtcNow));

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(2, issue.OccurrenceCount);
        Assert.NotNull(issue.NextRemediationAfterUtc);
        Assert.Equal(
            future,
            DateTime.SpecifyKind(issue.NextRemediationAfterUtc!.Value, DateTimeKind.Utc),
            TimeSpan.FromSeconds(1));
    }

    private static SystemIssueRemediationDispatcher CreateDispatcher(
        PostgresTestDatabase db,
        IIssueRemediator remediator,
        SystemIssueOptions? options = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new SystemIssueRemediationDispatcher(
            dbContextFactory: db.CreateDbContextFactory(),
            remediators: new[] { remediator },
            auditPublisher: new NoopAuditEventPublisher(),
            scopeFactory: services.GetRequiredService<IServiceScopeFactory>(),
            options: Options.Create(options ?? new SystemIssueOptions
            {
                MaxRemediationAttempts = 3,
                RemediationBaseBackoff = TimeSpan.FromMilliseconds(1),
                RemediationMaxBackoff = TimeSpan.FromMilliseconds(1)
            }),
            logger: NullLogger<SystemIssueRemediationDispatcher>.Instance);
    }

    private static async Task<AuditOutboxEntry> SeedDeadLetterAsync(
        PostgresTestDatabase db, int attemptCount, string topic, string eventType, string? lastError)
    {
        await using var seed = db.CreateDbContext();
        var row = new AuditOutboxEntry
        {
            Topic = topic,
            EventType = eventType,
            PayloadJson = "{\"hello\":1}",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            NextAttemptAfterUtc = DateTime.UtcNow,
            AttemptCount = attemptCount,
            LastError = lastError
        };
        seed.AuditOutbox.Add(row);
        await seed.SaveChangesAsync();
        return row;
    }

    // Synthetic remediator that always fails — exercises the
    // attempt-cap-and-give-up path.
    private sealed class AlwaysFailRemediator : IIssueRemediator
    {
        public const string DetectorIdValue = "test.always_fail";
        public string DetectorId => DetectorIdValue;
        public Task<RemediationResult> TryRemediateAsync(SystemIssue issue, CancellationToken cancellationToken) =>
            Task.FromResult<RemediationResult>(new RemediationResult.Failure("synthetic always-fail"));
    }
}
