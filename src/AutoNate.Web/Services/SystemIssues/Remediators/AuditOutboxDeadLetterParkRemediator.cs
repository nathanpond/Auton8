using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.SystemIssues.Detectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AutoNate.Web.Services.SystemIssues.Remediators;

// Pairs with AuditOutboxDeadLetterDetector: when the dispatcher abandons an
// audit_outbox row at MaxAttempts the detector opens an issue, and this
// remediator moves the row to audit_outbox_dead_letters and deletes it from
// the live table. Safe because:
//
// * The dispatcher already gave up — there is no active retry to race.
// * The audit_outbox row is preserved in audit_outbox_dead_letters with a
//   reason and the original payload, so forensics is still possible.
// * The fingerprint scheme guarantees one issue per row, so re-runs on the
//   same issue are idempotent (the row is gone after the first run; the
//   remediator returns Skip).
public sealed class AuditOutboxDeadLetterParkRemediator(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<AuditOutboxDeadLetterParkRemediator> logger) : IIssueRemediator
{
    public string DetectorId => AuditOutboxDeadLetterDetector.DetectorIdValue;

    public async Task<RemediationResult> TryRemediateAsync(SystemIssue issue, CancellationToken cancellationToken)
    {
        // Identify the row from facts_json (the detector embeds outboxRowId).
        // Fall back to RelatedEntityId if facts is unparseable for any reason.
        if (!TryReadOutboxRowId(issue, out var outboxId))
        {
            return new RemediationResult.Skip(
                "Could not determine outbox row id from issue facts/relatedEntityId.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // INSERT … SELECT moves the row payload across atomically with the
        // delete. RETURNING tells us how many rows the INSERT actually
        // captured — zero means the audit_outbox row was already removed
        // (race with another remediator instance, or already remediated).
        var connection = (Npgsql.NpgsqlConnection)dbContext.Database.GetDbConnection();
        const string parkSql = """
            WITH moved AS (
                INSERT INTO audit_outbox_dead_letters (
                    original_outbox_id, topic, event_type, payload_json,
                    original_created_at_utc, attempt_count, last_error,
                    parked_reason
                )
                SELECT id, topic, event_type, payload_json, created_at_utc,
                       attempt_count, last_error, @parked_reason
                FROM audit_outbox
                WHERE id = @outbox_id
                RETURNING original_outbox_id
            )
            DELETE FROM audit_outbox
            WHERE id IN (SELECT original_outbox_id FROM moved)
            RETURNING id;
            """;

        await using (var command = new Npgsql.NpgsqlCommand(parkSql, connection))
        {
            command.Transaction = (Npgsql.NpgsqlTransaction)tx.GetDbTransaction();
            command.Parameters.AddWithValue("outbox_id", outboxId);
            command.Parameters.AddWithValue(
                "parked_reason",
                $"Parked by AuditOutboxDeadLetterParkRemediator after dispatcher abandoned at MaxAttempts (issue {issue.Id}).");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                // No row moved. Either it was already parked or it never
                // existed. Either way the issue can close — the dead-letter
                // condition is no longer present.
                await tx.CommitAsync(cancellationToken);
                logger.LogInformation(
                    "Outbox row {OutboxId} was not present; treating as already-parked for issue {IssueId}.",
                    outboxId, issue.Id);
                return new RemediationResult.Success(
                    Notes: "Outbox row was not present (already parked or removed externally).");
            }
        }

        await tx.CommitAsync(cancellationToken);
        return new RemediationResult.Success(
            Notes: $"Parked audit_outbox row {outboxId} into audit_outbox_dead_letters.");
    }

    private static bool TryReadOutboxRowId(SystemIssue issue, out long outboxId)
    {
        outboxId = 0;
        if (!string.IsNullOrEmpty(issue.FactsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(issue.FactsJson);
                if (doc.RootElement.TryGetProperty("outboxRowId", out var prop)
                    && prop.ValueKind == JsonValueKind.Number
                    && prop.TryGetInt64(out var fromFacts))
                {
                    outboxId = fromFacts;
                    return true;
                }
            }
            catch (JsonException) { /* fall through */ }
        }

        if (!string.IsNullOrEmpty(issue.RelatedEntityId)
            && long.TryParse(issue.RelatedEntityId, out var fromRelated))
        {
            outboxId = fromRelated;
            return true;
        }

        return false;
    }
}
