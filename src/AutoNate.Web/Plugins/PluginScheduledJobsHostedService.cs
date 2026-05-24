using AutoNate.Web.Services.Projections;

namespace AutoNate.Web.Plugins;

// One drain loop per plugin-registered scheduled job. Snapshots the
// registry at app start (jobs registered after that point need a restart
// to begin draining — documented limitation in IPluginProjections).
//
// Each tick records into ProjectionHealthService so plugin jobs show up
// on /api/admin/projections alongside built-in projections.
public sealed class PluginScheduledJobsHostedService : BackgroundService
{
    private readonly PluginScheduledJobRegistry _registry;
    private readonly IProjectionHealthService _health;
    private readonly ILogger<PluginScheduledJobsHostedService> _logger;

    public PluginScheduledJobsHostedService(
        PluginScheduledJobRegistry registry,
        IProjectionHealthService health,
        ILogger<PluginScheduledJobsHostedService> logger)
    {
        _registry = registry;
        _health = health;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobs = _registry.Snapshot();
        if (jobs.Count == 0) return;

        var tasks = jobs.Select(j => RunJobAsync(j, stoppingToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task RunJobAsync(PluginScheduledJob job, CancellationToken stoppingToken)
    {
        // Fire immediately on startup so admins see the row populate
        // without waiting one full interval — matches the polling-feed
        // behavior on the framework side.
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_health.IsPaused(job.Name))
            {
                try { await Task.Delay(job.Interval, stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await job.Tick(stoppingToken);
                _health.RecordApply(job.Name, "plugin.scheduled", eventCount: 1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _health.RecordFailure(job.Name, "plugin.scheduled", ex.Message);
                _logger.LogError(ex,
                    "Plugin scheduled job {Name} (plugin {PluginId}) failed; retrying after interval.",
                    job.Name, job.PluginId);
            }

            try { await Task.Delay(job.Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
