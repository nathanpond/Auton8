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
using SystemIssueEntity = AutoNate.Web.Persistence.Scaffolded.SystemIssue;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class OrphanReferenceDetectorTests
{
    [Fact]
    public async Task Notification_pointing_at_missing_record_opens_an_issue_and_remediator_deletes_it()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        // Seed an orphaned record-assignment notification (no backing record).
        var notificationId = Guid.NewGuid();
        await using (var seed = db.CreateDbContext())
        {
            await seed.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO notifications (id, user_id, kind, title, body, related_entity_kind, related_entity_id, link_path, is_read, created_at_utc)
                VALUES ({notificationId}, gen_random_uuid(), 'record.assigned', 'orphan', 'orphan body',
                        'record', {Guid.NewGuid().ToString()}, NULL, FALSE, NOW())");
        }

        var (detector, _) = CreateDetectorAndStore(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.StartsWith(OrphanReferenceDetector.FingerprintNotification, issue.Fingerprint);
        Assert.NotNull(issue.NextRemediationAfterUtc);

        var remediator = new OrphanReferenceRemediator(db.CreateDbContextFactory(), NullLogger<OrphanReferenceRemediator>.Instance);
        var result = await remediator.TryRemediateAsync(ToDomain(issue), CancellationToken.None);
        Assert.IsType<RemediationResult.Success>(result);

        // Notification row is gone.
        await using var conn = (NpgsqlConnection)read.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM notifications WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", notificationId);
        var remaining = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0L, remaining);
    }

    [Fact]
    public async Task Permission_grant_referencing_deleted_user_opens_an_issue_and_remediator_deletes_it()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        var grantId = Guid.NewGuid();
        await using (var seed = db.CreateDbContext())
        {
            await seed.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO permission_grants (id, principal_kind, principal_id, action, selector_string, selector_ast, effect, priority, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES ({grantId}, 'user', {Guid.NewGuid().ToString()}, 'view', '/record/*', '{{}}'::jsonb, 'allow', 0, NOW(), {Guid.Empty}, NOW(), {Guid.Empty})");
        }

        var (detector, _) = CreateDetectorAndStore(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.StartsWith(OrphanReferenceDetector.FingerprintPermissionGrantUser, issue.Fingerprint);

        var remediator = new OrphanReferenceRemediator(db.CreateDbContextFactory(), NullLogger<OrphanReferenceRemediator>.Instance);
        var result = await remediator.TryRemediateAsync(ToDomain(issue), CancellationToken.None);
        Assert.IsType<RemediationResult.Success>(result);

        await using var conn = (NpgsqlConnection)read.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM permission_grants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", grantId);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Menu_item_with_dead_plugin_id_opens_issue_and_remediator_clears_the_column()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        // Find an existing menu_id (the seed creates the standard menus).
        Guid menuItemId;
        await using (var seed = db.CreateDbContext())
        {
            var existingMenuId = await seed.Menus.AsNoTracking().Select(m => m.Id).FirstAsync();
            menuItemId = Guid.NewGuid();
            await seed.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO menu_items (id, menu_id, parent_id, sort_order, display_name, icon, item_type, config, is_visible, is_system, created_at_utc, updated_at_utc, created_by_plugin_id)
                VALUES ({menuItemId}, {existingMenuId}, NULL, 999, 'Orphan', NULL,
                        'route', '{{""path"":""/x""}}'::jsonb, TRUE, FALSE, NOW(), NOW(), {Guid.NewGuid()})");
        }

        var (detector, _) = CreateDetectorAndStore(db);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.StartsWith(OrphanReferenceDetector.FingerprintMenuItemPlugin, issue.Fingerprint);

        var remediator = new OrphanReferenceRemediator(db.CreateDbContextFactory(), NullLogger<OrphanReferenceRemediator>.Instance);
        var result = await remediator.TryRemediateAsync(ToDomain(issue), CancellationToken.None);
        Assert.IsType<RemediationResult.Success>(result);

        // Menu item still exists, but created_by_plugin_id is now null.
        await using var conn = (NpgsqlConnection)read.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT created_by_plugin_id FROM menu_items WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", menuItemId);
        var pluginId = await cmd.ExecuteScalarAsync();
        Assert.True(pluginId is null || pluginId is DBNull);
    }

    [Fact]
    public async Task Detector_reruns_against_an_open_issue_dedup_to_one_row()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await using (var seed = db.CreateDbContext())
        {
            await seed.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO notifications (id, user_id, kind, title, body, related_entity_kind, related_entity_id, link_path, is_read, created_at_utc)
                VALUES ({Guid.NewGuid()}, gen_random_uuid(), 'record.assigned', 'orphan', 'orphan body',
                        'record', {Guid.NewGuid().ToString()}, NULL, FALSE, NOW())");
        }
        var (detector, _) = CreateDetectorAndStore(db);

        await detector.RunOnceAsync(CancellationToken.None);
        await detector.RunOnceAsync(CancellationToken.None);
        await detector.RunOnceAsync(CancellationToken.None);

        await using var read = db.CreateDbContext();
        var issue = Assert.Single(await read.SystemIssues.AsNoTracking().ToListAsync());
        Assert.Equal(3, issue.OccurrenceCount);
    }

    private static (OrphanReferenceDetector detector, EfCoreSystemIssueStore store) CreateDetectorAndStore(PostgresTestDatabase db)
    {
        var store = new EfCoreSystemIssueStore(db.CreateDbContextFactory(), new NoopAuditEventPublisher(), new NoopCriticalIssueNotifier());
        var detector = new OrphanReferenceDetector(
            db.CreateDbContextFactory(), store,
            Options.Create(new OrphanReferenceDetectorOptions { BatchSizePerClass = 100 }),
            Options.Create(new SystemIssueOptions { DetectorsEnabled = false }),
            NullLogger<OrphanReferenceDetector>.Instance);
        return (detector, store);
    }

    private static SystemIssue ToDomain(SystemIssueEntity row) => new(
        row.Id, row.DetectorId, row.Category, row.Severity, row.Fingerprint,
        row.Title, row.Summary, row.RelatedEntityKind, row.RelatedEntityId, row.FactsJson, row.State,
        new DateTimeOffset(DateTime.SpecifyKind(row.FirstSeenAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(row.LastSeenAtUtc, DateTimeKind.Utc)),
        row.OccurrenceCount,
        row.AcknowledgedAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.AcknowledgedAtUtc.Value, DateTimeKind.Utc)) : null,
        row.AcknowledgedBy,
        row.ResolvedAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.ResolvedAtUtc.Value, DateTimeKind.Utc)) : null,
        row.ResolutionKind, row.ResolutionNotes,
        row.AutoRemediationAttemptCount, row.AutoRemediationLastError,
        row.NextRemediationAfterUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.NextRemediationAfterUtc.Value, DateTimeKind.Utc)) : null);
}
