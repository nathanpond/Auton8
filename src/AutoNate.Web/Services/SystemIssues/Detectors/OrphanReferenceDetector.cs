using System.Text.Json;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Periodic sweeper that scans for rows referencing entities that no longer
// exist. Several AutoNate tables rely on application-level invariants rather
// than FK enforcement (notifications, permission_grants, menu_items.plugin_id),
// so drift naturally accumulates — usually small, but invisible until you go
// looking. This detector makes it visible by class and lets the paired
// OrphanReferenceRemediator clean it up.
//
// Fingerprint prefix per class (`orphan:<class>:<id>`) lets the remediator
// route by class. The detector caps each class per tick so a one-off backlog
// doesn't drop thousands of issues at once.
public sealed class OrphanReferenceDetector(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ISystemIssueRecorder recorder,
    IOptions<OrphanReferenceDetectorOptions> orphanOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<OrphanReferenceDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly OrphanReferenceDetectorOptions _orphanOptions = orphanOptions.Value;

    public const string DetectorIdValue = "orphan_reference";

    // Fingerprint prefixes the remediator routes on. Public so the remediator
    // doesn't duplicate the constants.
    public const string FingerprintNotification = "orphan:notification:";
    public const string FingerprintPermissionGrantUser = "orphan:permission_grant:user:";
    public const string FingerprintPermissionGrantRole = "orphan:permission_grant:role:";
    public const string FingerprintMenuItemPlugin = "orphan:menu_item:plugin_id:";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _orphanOptions.Interval;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await ScanRecordNotificationOrphansAsync(connection, cancellationToken);
        await ScanPermissionGrantUserOrphansAsync(connection, cancellationToken);
        await ScanPermissionGrantRoleOrphansAsync(connection, cancellationToken);
        await ScanMenuItemPluginOrphansAsync(connection, cancellationToken);
    }

    // notifications.kind = 'record.assigned' whose backing record is gone or
    // whose user is no longer in assignee_ids. Mirrors the criteria that
    // OrphanedNotificationCleanupService uses for its one-shot scan, but
    // applied continuously and one-issue-per-row so the remediator can act
    // surgically.
    private async Task ScanRecordNotificationOrphansAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT n.id, n.user_id, n.related_entity_id
            FROM notifications n
            WHERE n.kind = 'record.assigned'
              AND n.related_entity_kind = 'record'
              AND n.related_entity_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM records r
                  WHERE r.id::text = n.related_entity_id
                    AND r.is_archived = FALSE
                    AND n.user_id = ANY (r.assignee_ids)
              )
            LIMIT @batch_size;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch_size", _orphanOptions.BatchSizePerClass);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var notificationId = reader.GetGuid(0);
            var userId = reader.GetGuid(1);
            var recordId = await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2);
            await OpenIssueAsync(
                fingerprint: FingerprintNotification + notificationId,
                title: "Orphaned record-assignment notification",
                summary: $"Notification {notificationId} for user {userId} references record {recordId} which is archived, deleted, or no longer lists that user as an assignee.",
                relatedEntityKind: "notification",
                relatedEntityId: notificationId.ToString(),
                facts: new
                {
                    notificationId,
                    userId,
                    recordId,
                    notificationKind = "record.assigned"
                },
                cancellationToken);
        }
    }

    private async Task ScanPermissionGrantUserOrphansAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // permission_grants.principal_kind='user' whose principal_id no
        // longer matches a row in local_users (compared against the public
        // user_id column, which is what the grants store).
        const string sql = """
            SELECT pg.id, pg.principal_id, pg.action
            FROM permission_grants pg
            WHERE pg.principal_kind = 'user'
              AND NOT EXISTS (
                  SELECT 1 FROM local_users u
                  WHERE u.user_id::text = pg.principal_id
              )
            LIMIT @batch_size;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch_size", _orphanOptions.BatchSizePerClass);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var grantId = reader.GetGuid(0);
            var principalId = reader.GetString(1);
            var action = reader.GetString(2);
            await OpenIssueAsync(
                fingerprint: FingerprintPermissionGrantUser + grantId,
                title: "Permission grant references deleted user",
                summary: $"Grant {grantId} (action '{action}') is held by user {principalId}, who no longer exists in local_users.",
                relatedEntityKind: "permission_grant",
                relatedEntityId: grantId.ToString(),
                facts: new
                {
                    grantId,
                    principalKind = "user",
                    principalId,
                    action
                },
                cancellationToken);
        }
    }

    private async Task ScanPermissionGrantRoleOrphansAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT pg.id, pg.principal_id, pg.action
            FROM permission_grants pg
            WHERE pg.principal_kind = 'role'
              AND NOT EXISTS (
                  SELECT 1 FROM roles r
                  WHERE r.id::text = pg.principal_id
              )
            LIMIT @batch_size;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch_size", _orphanOptions.BatchSizePerClass);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var grantId = reader.GetGuid(0);
            var principalId = reader.GetString(1);
            var action = reader.GetString(2);
            await OpenIssueAsync(
                fingerprint: FingerprintPermissionGrantRole + grantId,
                title: "Permission grant references deleted role",
                summary: $"Grant {grantId} (action '{action}') is held by role {principalId}, which no longer exists.",
                relatedEntityKind: "permission_grant",
                relatedEntityId: grantId.ToString(),
                facts: new
                {
                    grantId,
                    principalKind = "role",
                    principalId,
                    action
                },
                cancellationToken);
        }
    }

    private async Task ScanMenuItemPluginOrphansAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // menu_items.created_by_plugin_id pointing at a plugins row that no
        // longer exists. Surfaces leftover menu items after a plugin is
        // deleted that didn't clean up after itself. Remediator clears the
        // column rather than deleting the menu item — operators may want to
        // keep the entry visible.
        const string sql = """
            SELECT mi.id, mi.created_by_plugin_id, mi.display_name
            FROM menu_items mi
            WHERE mi.created_by_plugin_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM plugins p WHERE p.id = mi.created_by_plugin_id
              )
            LIMIT @batch_size;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("batch_size", _orphanOptions.BatchSizePerClass);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var menuItemId = reader.GetGuid(0);
            var pluginId = reader.GetGuid(1);
            var displayName = reader.GetString(2);
            await OpenIssueAsync(
                fingerprint: FingerprintMenuItemPlugin + menuItemId,
                title: $"Menu item references deleted plugin",
                summary: $"Menu item '{displayName}' ({menuItemId}) was created by plugin {pluginId}, which no longer exists. The remediator clears created_by_plugin_id; the menu item itself stays.",
                relatedEntityKind: "menu_item",
                relatedEntityId: menuItemId.ToString(),
                facts: new
                {
                    menuItemId,
                    pluginId,
                    displayName
                },
                cancellationToken);
        }
    }

    private Task OpenIssueAsync(
        string fingerprint,
        string title,
        string summary,
        string relatedEntityKind,
        string relatedEntityId,
        object facts,
        CancellationToken cancellationToken)
    {
        return recorder.RecordAsync(new SystemIssueDraft(
            DetectorId: DetectorIdValue,
            Category: SystemIssueCategories.DataIntegrity,
            Severity: SystemIssueSeverities.Warning,
            Fingerprint: fingerprint,
            Title: title,
            Summary: summary,
            RelatedEntityKind: relatedEntityKind,
            RelatedEntityId: relatedEntityId,
            FactsJson: JsonSerializer.Serialize(facts),
            // Opt into auto-remediation. The dispatcher routes to
            // OrphanReferenceRemediator on its next tick.
            RemediationDueAtUtc: DateTime.UtcNow), cancellationToken);
    }
}

public sealed class OrphanReferenceDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:OrphanReference";

    // The plan calls for 30-minute cadence — orphans are slow-changing and
    // the remediator doesn't need urgency. Tunable via configuration.
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    // Per-class cap so a one-off backlog doesn't dump thousands of new issues
    // in a single tick. Subsequent ticks pick up the rest.
    public int BatchSizePerClass { get; set; } = 100;
}
