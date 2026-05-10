using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Periodic. Looks for workflow_execution_errors rows whose process instance
// is still running in Flowable — those are real "this process is stuck on a
// failed job" issues that an operator should know about. Errors against
// completed/cancelled processes are out of scope (the process moved on).
//
// One issue per process instance (not per error row): a stuck process often
// produces dozens of retry errors, and folding them into one issue keeps the
// System Issues page useful. The most-recent error message is surfaced as
// the summary; the detector tick re-records the issue (occurrence_count
// bumps) when new errors come in.
public sealed class WorkflowExecutionErrorOpenDetector(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IFlowableClient flowableClient,
    ISystemIssueRecorder recorder,
    IOptions<WorkflowExecutionErrorOpenDetectorOptions> openOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<WorkflowExecutionErrorOpenDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly WorkflowExecutionErrorOpenDetectorOptions _openOptions = openOptions.Value;

    public const string DetectorIdValue = "workflow_execution_error_open";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _openOptions.Interval;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var aggregated = await LoadAggregatedAsync(cancellationToken);

        foreach (var group in aggregated)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip if the Flowable instance is gone or completed — the
            // process moved past the failed node, so the error is no longer
            // an active concern.
            FlowableProcessInstanceSummary? instance;
            try
            {
                instance = await flowableClient.GetProcessInstanceAsync(group.ProcessInstanceId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Same discipline as OrphanedNotificationCleanupService: skip
                // rather than misclassify when Flowable is temporarily down.
                logger.LogWarning(ex,
                    "WorkflowExecutionErrorOpenDetector: skipping {ProcessInstanceId} — Flowable lookup failed.",
                    group.ProcessInstanceId);
                continue;
            }

            if (instance is null)
            {
                // Process is gone. Resolve any open issue — the condition is
                // no longer present.
                await recorder.MarkResolvedByFingerprintAsync(
                    FingerprintFor(group.ProcessInstanceId),
                    SystemIssueResolutionKinds.NoLongerPresent,
                    notes: "Process instance no longer exists in Flowable.",
                    cancellationToken);
                continue;
            }

            var facts = JsonSerializer.Serialize(new
            {
                processInstanceId = group.ProcessInstanceId,
                errorCount = group.ErrorCount,
                mostRecentActivityId = group.MostRecent.ActivityId,
                mostRecentActivityName = group.MostRecent.ActivityName,
                mostRecentOccurredAtUtc = group.MostRecent.OccurredAtUtc,
                mostRecentRawFlowableEventType = group.MostRecent.RawFlowableEventType
            });

            await recorder.RecordAsync(new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.Workflow,
                Severity: SystemIssueSeverities.Error,
                Fingerprint: FingerprintFor(group.ProcessInstanceId),
                Title: $"Workflow execution failure on process {group.ProcessInstanceId}",
                Summary: group.MostRecent.ErrorMessage
                    ?? $"{group.ErrorCount} error(s) recorded; most recent at activity '{group.MostRecent.ActivityName ?? group.MostRecent.ActivityId}'.",
                RelatedEntityKind: "workflow_execution",
                RelatedEntityId: group.ProcessInstanceId,
                FactsJson: facts), cancellationToken);
        }
    }

    // Per-process aggregation done via raw SQL: EF Core can't translate the
    // "most recent row per group" pattern (`g.OrderByDescending(...).First()`)
    // into Postgres. DISTINCT ON does it cleanly in one round-trip.
    private async Task<List<ProcessErrorGroup>> LoadAggregatedAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        const string sql = """
            SELECT
                most_recent.process_instance_id,
                most_recent.activity_id,
                most_recent.activity_name,
                most_recent.error_message,
                most_recent.raw_flowable_event_type,
                most_recent.occurred_at_utc,
                counts.error_count
            FROM (
                SELECT DISTINCT ON (process_instance_id)
                    process_instance_id, activity_id, activity_name, error_message,
                    raw_flowable_event_type, occurred_at_utc
                FROM workflow_execution_errors
                ORDER BY process_instance_id, occurred_at_utc DESC
            ) AS most_recent
            JOIN (
                SELECT process_instance_id, COUNT(*) AS error_count
                FROM workflow_execution_errors
                GROUP BY process_instance_id
            ) AS counts USING (process_instance_id)
            ORDER BY most_recent.occurred_at_utc DESC
            LIMIT @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", _openOptions.MaxProcessesPerTick);

        var results = new List<ProcessErrorGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Hoist the IsDBNull checks into locals so the awaits don't sit
            // inside the record constructor's argument list. Same wire calls,
            // tidier shape.
            var activityNameNull = await reader.IsDBNullAsync(2, cancellationToken);
            var errorMessageNull = await reader.IsDBNullAsync(3, cancellationToken);
            var rawTypeNull = await reader.IsDBNullAsync(4, cancellationToken);
            results.Add(new ProcessErrorGroup(
                ProcessInstanceId: reader.GetString(0),
                MostRecent: new ProcessErrorRow(
                    ActivityId: reader.GetString(1),
                    ActivityName: activityNameNull ? null : reader.GetString(2),
                    ErrorMessage: errorMessageNull ? null : reader.GetString(3),
                    RawFlowableEventType: rawTypeNull ? null : reader.GetString(4),
                    OccurredAtUtc: reader.GetDateTime(5)),
                ErrorCount: (int)reader.GetInt64(6)));
        }
        return results;
    }

    private sealed record ProcessErrorGroup(string ProcessInstanceId, ProcessErrorRow MostRecent, int ErrorCount);
    private sealed record ProcessErrorRow(
        string ActivityId, string? ActivityName, string? ErrorMessage,
        string? RawFlowableEventType, DateTime OccurredAtUtc);

    public static string FingerprintFor(string processInstanceId) =>
        $"workflow:execution_error:{processInstanceId}";
}

public sealed class WorkflowExecutionErrorOpenDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:WorkflowExecutionErrorOpen";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    // Per-tick cap so a Flowable misconfiguration that errors every job
    // doesn't dump thousands of issues at once. Subsequent ticks pick up
    // the next batch by recency.
    public int MaxProcessesPerTick { get; set; } = 100;
}
