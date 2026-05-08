using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.SystemIssues;

// Hooks the two CLR-level "exception escaped to the runtime" events so
// background-thread crashes (BackgroundService loops, fire-and-forget tasks,
// thread-pool callbacks) are recorded as SystemIssues alongside HTTP-pipeline
// exceptions caught by UnhandledExceptionRecording.
//
// Both handlers are best-effort: failures here must never themselves throw,
// or we'd cascade into another unhandled exception. All paths swallow.
public sealed class BackgroundExceptionTrap(
    ISystemIssueRecorder recorder,
    ILogger<BackgroundExceptionTrap> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
        return Task.CompletedTask;
    }

    private void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is not Exception ex) return;

            // When IsTerminating, the host is about to kill the process, so
            // the recorder has a small window to flush. Cap the wait so a
            // wedged DB doesn't hold up shutdown indefinitely. Sync-over-
            // async is acceptable here: the thread is already on its way out.
            try
            {
                var task = Task.Run(() => UnhandledExceptionRecorder.RecordAppDomainAsync(
                    recorder, ex, e.IsTerminating, CancellationToken.None));
                task.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception recordEx)
            {
                logger.LogError(recordEx, "Failed to record AppDomain unhandled exception.");
            }
        }
        catch
        {
            // Swallow: process is already in trouble; never compound it.
        }
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            // AggregateException with a single inner is the common case
            // (await rethrows the inner) — unwrap so the fingerprint matches
            // the exception the developer would actually see.
            var ex = e.Exception.InnerExceptions.Count == 1
                ? e.Exception.InnerExceptions[0]
                : (Exception)e.Exception;

            // Process is not dying; fire-and-forget is fine.
            _ = Task.Run(async () =>
            {
                try
                {
                    await UnhandledExceptionRecorder.RecordUnobservedTaskAsync(
                        recorder, ex, CancellationToken.None);
                }
                catch (Exception recordEx)
                {
                    logger.LogError(recordEx, "Failed to record unobserved task exception.");
                }
            });

            e.SetObserved();
        }
        catch
        {
            // Swallow: same rationale as AppDomain handler.
        }
    }
}
