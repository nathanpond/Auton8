using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.SystemIssues.Detectors;

// Base class for time-driven detectors. Concrete subclasses implement
// RunOnceAsync (the actual probe) and expose a DetectorId + Interval. The
// base class owns the loop discipline that all detectors must share:
//
// * Master switch — SystemIssues:DetectorsEnabled = false makes the loop
//   no-op. Tests opt out of detector ticks via this knob.
// * Exception isolation — one bad tick must not kill the service. The loop
//   catches anything except cancellation and logs it. Mirrors
//   AuditOutboxDispatcher.ExecuteAsync.
// * RunOnceAsync is public so tests can drive a single tick without
//   spinning up the BackgroundService. Same pattern as
//   AuditOutboxDispatcher.DispatchBatchAsync.
public abstract class PeriodicIssueDetector(
    IOptions<SystemIssueOptions> systemIssueOptions,
    ILogger logger) : BackgroundService
{
    private readonly SystemIssueOptions _options = systemIssueOptions.Value;

    public abstract string DetectorId { get; }

    public abstract TimeSpan Interval { get; }

    // The detector's actual work. Implementations should be idempotent —
    // record/resolve operations on the issue store dedup by fingerprint.
    public abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.DetectorsEnabled)
        {
            logger.LogInformation(
                "Detector {DetectorId} disabled via {Section}:DetectorsEnabled.",
                DetectorId, SystemIssueOptions.SectionName);
            return;
        }

        // Tiny initial stagger so multiple detectors registered at once
        // don't all probe at the same wall-clock millisecond on startup.
        // Not security-critical — just avoids thundering herds against
        // SystemHealthService and the audit_outbox table.
        try
        {
            await Task.Delay(InitialStagger(), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Detectors must never die. The fingerprint dedup means even
                // a flapping condition is bounded to one row per detector.
                logger.LogError(ex,
                    "Detector {DetectorId} tick failed; retrying after Interval.",
                    DetectorId);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    // Override in subclasses if a particular detector needs a different
    // startup delay (e.g. the snapshot detector wants to probe immediately).
    protected virtual TimeSpan InitialStagger() => TimeSpan.FromSeconds(5);
}
