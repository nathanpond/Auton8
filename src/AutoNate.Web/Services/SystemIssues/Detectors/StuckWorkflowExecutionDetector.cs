using System.Text.Json;
using AutoNate.Web.Services.Flowable;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Periodic. Asks Flowable for the full executions list, filters to running
// instances whose LastActivityAtUtc is older than the configured threshold
// (or whose StartedAtUtc is old and have no activity ever), and opens one
// issue per stuck process. The plan called for 5-min cadence — quick enough
// that an operator notices a stuck workflow inside a coffee break.
//
// "Stuck" here is "active and not progressing" — exactly the failure mode
// where a missing service-task callback or hung async job leaves a process
// alive but unable to complete. Auto-remediation isn't safe because the
// right call (cancel? signal? wait?) is process-specific.
public sealed class StuckWorkflowExecutionDetector(
    IFlowableClient flowableClient,
    ISystemIssueRecorder recorder,
    ISystemIssueStore issueStore,
    IOptions<StuckWorkflowExecutionDetectorOptions> stuckOptions,
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger<StuckWorkflowExecutionDetector> logger)
    : PeriodicIssueDetector(systemIssueOptions, logger)
{
    private readonly StuckWorkflowExecutionDetectorOptions _stuckOptions = stuckOptions.Value;

    public const string DetectorIdValue = "stuck_workflow_execution";

    public override string DetectorId => DetectorIdValue;

    public override TimeSpan Interval => _stuckOptions.Interval;

    public override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Models.WorkflowExecutionSummary> executions;
        try
        {
            executions = await flowableClient.GetWorkflowExecutionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Flowable down: skip this tick rather than open a bunch of
            // false-positive issues on the next one. Mirrors the
            // OrphanedNotificationCleanupService discipline.
            logger.LogWarning(ex,
                "StuckWorkflowExecutionDetector: skipping tick — Flowable lookup failed.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var staleAfter = now - _stuckOptions.StaleAfter;

        var stuckThisTick = new HashSet<string>(StringComparer.Ordinal);
        foreach (var execution in executions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(execution.Status, "Running", StringComparison.Ordinal))
            {
                continue;
            }

            // No activity ever: only stuck once the StartedAt is old enough.
            // Activity at all: only stuck if last activity is older than threshold.
            var lastChange = execution.LastActivityAtUtc ?? execution.StartedAtUtc;
            if (lastChange is null || lastChange > staleAfter)
            {
                continue;
            }

            // A process parked at a user task is waiting on a human, not
            // stuck — user tasks don't have a SLA the engine enforces. Skip
            // any process with at least one active task. Catches the common
            // false positive without extra config. (Same shape would catch
            // intermediate catch events too if Flowable surfaced them as
            // "tasks", which it doesn't — those still trip the detector. A
            // future refinement could check job/event subscriptions.)
            try
            {
                var activeTasks = await flowableClient.GetTasksByProcessInstanceAsync(
                    execution.Id, cancellationToken);
                if (activeTasks.Count > 0)
                {
                    continue;
                }
            }
            catch (Exception ex)
            {
                // Don't open a false-positive issue when we can't even
                // verify whether the process has open user tasks — the
                // SystemHealthSnapshotDetector already covers Flowable
                // outages, so skipping this candidate is the safe call.
                logger.LogWarning(ex,
                    "StuckWorkflowExecutionDetector: skipping {ProcessInstanceId} — task lookup failed.",
                    execution.Id);
                continue;
            }

            var fingerprint = FingerprintFor(execution.Id);
            stuckThisTick.Add(fingerprint);

            await recorder.RecordAsync(new SystemIssueDraft(
                DetectorId: DetectorIdValue,
                Category: SystemIssueCategories.Workflow,
                Severity: SystemIssueSeverities.Warning,
                Fingerprint: fingerprint,
                Title: $"Workflow execution '{execution.Name ?? execution.WorkflowModelName ?? execution.Id}' has been idle for over {(int)_stuckOptions.StaleAfter.TotalMinutes} min",
                Summary: $"Process {execution.Id} (status={execution.Status}) last activity at {lastChange:O}; current step '{execution.CurrentStep ?? "(none)"}'.",
                RelatedEntityKind: "workflow_execution",
                RelatedEntityId: execution.Id,
                FactsJson: JsonSerializer.Serialize(new
                {
                    processInstanceId = execution.Id,
                    name = execution.Name,
                    workflowModelName = execution.WorkflowModelName,
                    startedAtUtc = execution.StartedAtUtc,
                    lastActivityAtUtc = execution.LastActivityAtUtc,
                    currentStep = execution.CurrentStep,
                    staleAfterMinutes = (int)_stuckOptions.StaleAfter.TotalMinutes
                })));
        }

        // Auto-resolve: query the DB for every issue we currently own
        // (open or acknowledged), then close anything that didn't reappear
        // in this tick's stuck set. Querying the DB instead of an in-
        // memory `_previousFingerprints` set means an issue stranded by an
        // app restart still gets resolved — the previous in-memory set
        // would be empty after a restart and stuck issues would persist
        // even after the underlying condition cleared.
        var openInDb = await issueStore.ListOpenFingerprintsForDetectorAsync(DetectorIdValue, cancellationToken);
        foreach (var fingerprint in openInDb)
        {
            if (stuckThisTick.Contains(fingerprint)) continue;
            await recorder.MarkResolvedByFingerprintAsync(
                fingerprint,
                SystemIssueResolutionKinds.NoLongerPresent,
                notes: "Process is no longer in the stuck set (resumed, completed, no longer running, or now waiting on a user task / external action).",
                cancellationToken);
        }
    }

    public static string FingerprintFor(string processInstanceId) =>
        $"workflow:stuck:{processInstanceId}";
}

public sealed class StuckWorkflowExecutionDetectorOptions
{
    public const string SectionName = "SystemIssues:Detectors:StuckWorkflowExecution";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    // A process counts as stuck if its last activity (or start time when it
    // never moved) is older than this. 30 min is conservative for human-
    // task-heavy workflows; tune via configuration for installs that have
    // longer-running async work.
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(30);
}
