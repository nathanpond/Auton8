using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SystemIssues;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SystemIssueStoreTests
{
    [Fact]
    public async Task RecordAsync_inserts_a_new_issue_when_fingerprint_is_unseen()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        var result = await store.RecordAsync(new SystemIssueDraft(
            DetectorId: "test.detector",
            Category: SystemIssueCategories.Bus,
            Severity: SystemIssueSeverities.Warning,
            Fingerprint: "test:one",
            Title: "First sighting"));

        Assert.True(result.WasCreated);
        Assert.Equal(1, result.OccurrenceCount);
        Assert.NotEqual(Guid.Empty, result.IssueId);

        await using var read = db.CreateDbContext();
        var row = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal("test.detector", row.DetectorId);
        Assert.Equal("test:one", row.Fingerprint);
        Assert.Equal(SystemIssueStates.Open, row.State);
        Assert.Equal(1, row.OccurrenceCount);
    }

    [Fact]
    public async Task RecordAsync_dedups_to_same_row_when_fingerprint_already_open()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        var first = await store.RecordAsync(new SystemIssueDraft(
            DetectorId: "test.detector",
            Category: SystemIssueCategories.Bus,
            Severity: SystemIssueSeverities.Info,
            Fingerprint: "test:dup",
            Title: "Initial"));

        var second = await store.RecordAsync(new SystemIssueDraft(
            DetectorId: "test.detector",
            Category: SystemIssueCategories.Bus,
            Severity: SystemIssueSeverities.Warning,
            Fingerprint: "test:dup",
            Title: "Bumped"));

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.IssueId, second.IssueId);
        Assert.Equal(2, second.OccurrenceCount);

        await using var read = db.CreateDbContext();
        var row = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(2, row.OccurrenceCount);
        Assert.Equal(SystemIssueSeverities.Warning, row.Severity); // EXCLUDED.severity wins
        Assert.Equal("Bumped", row.Title);
    }

    [Fact]
    public async Task RecordAsync_after_resolve_opens_a_fresh_row()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        await store.RecordAsync(new SystemIssueDraft(
            "test.detector", SystemIssueCategories.Bus, SystemIssueSeverities.Warning,
            "test:reopen", "Initial"));
        var resolved = await store.MarkResolvedByFingerprintAsync(
            "test:reopen",
            SystemIssueResolutionKinds.NoLongerPresent,
            notes: null);
        Assert.NotNull(resolved);

        var second = await store.RecordAsync(new SystemIssueDraft(
            "test.detector", SystemIssueCategories.Bus, SystemIssueSeverities.Warning,
            "test:reopen", "Reoccurrence"));

        Assert.True(second.WasCreated);
        Assert.Equal(1, second.OccurrenceCount);

        await using var read = db.CreateDbContext();
        var rows = await read.SystemIssues.AsNoTracking().OrderBy(i => i.FirstSeenAtUtc).ToListAsync();
        Assert.Equal(2, rows.Count);
        // NoLongerPresent is a machine action (the detector noticed the
        // condition cleared), so it lands in auto_resolved.
        Assert.Equal(SystemIssueStates.AutoResolved, rows[0].State);
        Assert.Equal(SystemIssueStates.Open, rows[1].State);
    }

    [Fact]
    public async Task MarkResolvedByFingerprintAsync_returns_null_when_already_resolved()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        await store.RecordAsync(new SystemIssueDraft(
            "test.detector", SystemIssueCategories.Bus, SystemIssueSeverities.Warning,
            "test:idemp", "Initial"));
        var first = await store.MarkResolvedByFingerprintAsync(
            "test:idemp", SystemIssueResolutionKinds.Manual, notes: "fixed");
        Assert.NotNull(first);

        var second = await store.MarkResolvedByFingerprintAsync(
            "test:idemp", SystemIssueResolutionKinds.Manual, notes: "fixed again");
        Assert.Null(second);
    }

    [Fact]
    public async Task ListAsync_filters_by_state_severity_and_category()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        await store.RecordAsync(new SystemIssueDraft(
            "d", SystemIssueCategories.Bus, SystemIssueSeverities.Warning,
            "f:1", "warn-bus"));
        await store.RecordAsync(new SystemIssueDraft(
            "d", SystemIssueCategories.Workflow, SystemIssueSeverities.Critical,
            "f:2", "crit-workflow"));
        await store.RecordAsync(new SystemIssueDraft(
            "d", SystemIssueCategories.Workflow, SystemIssueSeverities.Info,
            "f:3", "info-workflow"));

        var openCritical = await store.ListAsync(new SystemIssueListQuery(
            State: SystemIssueStates.Open,
            Severity: SystemIssueSeverities.Critical));
        var workflows = await store.ListAsync(new SystemIssueListQuery(
            State: SystemIssueStates.Open,
            Category: SystemIssueCategories.Workflow));

        Assert.Single(openCritical);
        Assert.Equal("crit-workflow", openCritical[0].Title);
        Assert.Equal(2, workflows.Count);
    }

    [Fact]
    public async Task RecordAsync_after_acknowledged_still_dedups_to_same_row()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());

        var first = await store.RecordAsync(new SystemIssueDraft(
            "d", SystemIssueCategories.Auth, SystemIssueSeverities.Warning,
            "f:ack", "Initial"));

        // Simulate an operator hitting acknowledge (the API endpoint for this
        // lands in Phase 3; for the store-level test we update directly).
        await using (var seed = db.CreateDbContext())
        {
            var row = await seed.SystemIssues.FirstAsync(i => i.Id == first.IssueId);
            row.State = SystemIssueStates.Acknowledged;
            row.AcknowledgedAtUtc = DateTime.UtcNow;
            await seed.SaveChangesAsync();
        }

        var second = await store.RecordAsync(new SystemIssueDraft(
            "d", SystemIssueCategories.Auth, SystemIssueSeverities.Warning,
            "f:ack", "Bumped while ack'd"));

        Assert.False(second.WasCreated);
        Assert.Equal(first.IssueId, second.IssueId);
        Assert.Equal(2, second.OccurrenceCount);
    }
}
