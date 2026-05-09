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

    // Capacity 1 so bursts of RequestImmediateScan() calls coalesce into a
    // single wake-up. The loop drains the signal each tick by calling
    // WaitAsync with a timeout: returns true when a wake was requested
    // (next tick runs immediately), false when the Interval expired
    // (next tick on schedule).
    private readonly SemaphoreSlim _wakeSignal = new(initialCount: 0, maxCount: 1);

    public abstract string DetectorId { get; }

    public abstract TimeSpan Interval { get; }

    // The detector's actual work. Implementations should be idempotent —
    // record/resolve operations on the issue store dedup by fingerprint.
    public abstract Task RunOnceAsync(CancellationToken cancellationToken);

    // Wake the periodic loop early so the next tick runs ASAP instead of
    // waiting for the full Interval. Used by mutation paths that just
    // changed something a detector should re-scan (e.g. EfCoreMenuStore
    // calls this after every menu_item write so MisconfiguredMenuItemDetector
    // catches a bad row within seconds, not on the 30-min cadence).
    //
    // Coalesces: multiple calls between two ticks all collapse into a single
    // wake-up. Allocation-free fast path — safe to call from any request
    // thread without spawning Task.Run.
    public void RequestImmediateScan()
    {
        try { _wakeSignal.Release(); }
        catch (SemaphoreFullException) { /* a wake is already pending */ }
    }

    // Test-only peek. True when at least one RequestImmediateScan() call
    // hasn't been consumed by the loop yet. Lets unit tests assert that
    // a mutation path correctly wakes the detector without standing up the
    // BackgroundService loop. Don't read this from production code — the
    // loop's WaitAsync in ExecuteAsync is the canonical consumer.
    public bool IsImmediateScanRequested => _wakeSignal.CurrentCount > 0;

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
                // WaitAsync returns true when RequestImmediateScan() bumped
                // the semaphore (run the next tick immediately) and false
                // when the Interval expired (next tick on schedule). Either
                // way we proceed; the return value is irrelevant to the loop.
                await _wakeSignal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    // Override in subclasses if a particular detector needs a different
    // startup delay (e.g. the snapshot detector wants to probe immediately).
    protected virtual TimeSpan InitialStagger() => TimeSpan.FromSeconds(5);
}
