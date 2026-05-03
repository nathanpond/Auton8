using AutoNate.Web.Persistence;
using AutoNate.Web.Services.SystemIssues.Detectors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.SystemIssues.Remediators;

// Pairs with OrphanReferenceDetector. Each orphan class has a fingerprint
// prefix that this remediator routes on. Within each class the operation is a
// safe, scoped row delete (or a column reset for menu_items where we want to
// keep the entry visible).
//
// The remediator only acts on rows it can identify confidently — anything
// outside the recognised prefixes returns Skip rather than Failure so the
// dispatcher just stops polling that issue (no attempt-count burned).
public sealed class OrphanReferenceRemediator(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<OrphanReferenceRemediator> logger) : IIssueRemediator
{
    public string DetectorId => OrphanReferenceDetector.DetectorIdValue;

    public bool CanRemediate(SystemIssue issue) =>
        issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintNotification, StringComparison.Ordinal)
        || issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintPermissionGrantUser, StringComparison.Ordinal)
        || issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintPermissionGrantRole, StringComparison.Ordinal)
        || issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintMenuItemPlugin, StringComparison.Ordinal);

    public async Task<RemediationResult> TryRemediateAsync(SystemIssue issue, CancellationToken cancellationToken)
    {
        if (issue.RelatedEntityId is null || !Guid.TryParse(issue.RelatedEntityId, out var rowId))
        {
            return new RemediationResult.Skip(
                "Issue is missing the related row id needed to identify the orphan target.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintNotification, StringComparison.Ordinal))
        {
            return await DeleteAsync(connection, "DELETE FROM notifications WHERE id = @id;", rowId, "notification", cancellationToken);
        }
        if (issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintPermissionGrantUser, StringComparison.Ordinal)
            || issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintPermissionGrantRole, StringComparison.Ordinal))
        {
            return await DeleteAsync(connection, "DELETE FROM permission_grants WHERE id = @id;", rowId, "permission_grant", cancellationToken);
        }
        if (issue.Fingerprint.StartsWith(OrphanReferenceDetector.FingerprintMenuItemPlugin, StringComparison.Ordinal))
        {
            // Don't delete the menu item — clear created_by_plugin_id so the
            // entry stays usable. An operator can clean up the menu through
            // the SPA; the goal here is just to break the dangling link.
            return await DeleteAsync(connection,
                "UPDATE menu_items SET created_by_plugin_id = NULL WHERE id = @id;",
                rowId, "menu_item", cancellationToken);
        }

        return new RemediationResult.Skip(
            $"Fingerprint '{issue.Fingerprint}' does not match a known orphan class.");
    }

    private async Task<RemediationResult> DeleteAsync(
        NpgsqlConnection connection,
        string sql,
        Guid rowId,
        string entityKind,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", rowId);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            // Zero affected = the row is already gone (someone else deleted
            // it, or the previous run already cleaned it up). Treat as
            // success — the orphan condition is no longer present either way.
            return new RemediationResult.Success(
                Notes: affected == 0
                    ? $"{entityKind} row {rowId} was already absent."
                    : $"Removed orphan {entityKind} row {rowId}.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to remediate orphan {EntityKind} row {RowId}.",
                entityKind, rowId);
            return new RemediationResult.Failure(ex.Message);
        }
    }
}
