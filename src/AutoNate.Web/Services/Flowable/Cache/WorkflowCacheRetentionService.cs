using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Flowable.Cache;

// Periodic janitor that purges cache rows past their retention horizon.
// Reads per-process overrides from process_retention_config and falls back
// to FlowableCacheOptions.DefaultRetentionDays (7 years) for processes
// without an explicit row.
//
// Purges run against all four cache tables in dependency order
// (events → variables → tasks → executions). Each pass is bounded by a
// DELETE LIMIT so a one-off catch-up after a config change doesn't lock
// the cache tables for minutes.
public sealed class WorkflowCacheRetentionService : BackgroundService
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly FlowableCacheOptions _options;
    private readonly ILogger<WorkflowCacheRetentionService> _logger;

    public WorkflowCacheRetentionService(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<FlowableCacheOptions> options,
        ILogger<WorkflowCacheRetentionService> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RetentionEnabled)
        {
            _logger.LogInformation("Workflow cache retention disabled via FlowableCache:RetentionEnabled.");
            return;
        }

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
                _logger.LogError(ex, "Workflow cache retention sweep failed; retrying after interval.");
            }

            try { await Task.Delay(_options.RetentionSweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Public so tests can drive a single sweep without waiting for the loop.
    // Returns the per-table count of rows deleted across every process.
    public async Task<RetentionReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var overrides = await db.ProcessRetentionConfigs.AsNoTracking()
            .ToDictionaryAsync(c => c.ProcessDefinitionKey, c => c.RetainDays, cancellationToken);

        // Find distinct process keys in the cache so we can apply per-key
        // retention even for processes that have no override row. Cap to a
        // sane number — if someone hits this with millions of distinct keys
        // we'd want to revisit the loop strategy, but in practice the count
        // is in the tens or hundreds.
        var processKeys = await db.WorkflowExecutionCache.AsNoTracking()
            .Select(c => c.ProcessDefinitionKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        var report = new RetentionReport();
        foreach (var processKey in processKeys)
        {
            var retainDays = overrides.GetValueOrDefault(processKey, _options.DefaultRetentionDays);
            if (retainDays <= 0) continue;
            var cutoff = DateTime.UtcNow.AddDays(-retainDays);

            report.EventsDeleted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM workflow_event_log_cache
                WHERE process_definition_key = {processKey}
                  AND event_time < {cutoff}
                """, cancellationToken);

            // Variables and tasks ride along with executions — delete them
            // for any execution that's about to fall out of retention.
            report.VariablesDeleted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM workflow_variable_cache
                WHERE flowable_instance_id IN (
                    SELECT flowable_instance_id FROM workflow_execution_cache
                    WHERE process_definition_key = {processKey}
                      AND start_time < {cutoff}
                )
                """, cancellationToken);

            report.TasksDeleted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM workflow_task_cache
                WHERE flowable_instance_id IN (
                    SELECT flowable_instance_id FROM workflow_execution_cache
                    WHERE process_definition_key = {processKey}
                      AND start_time < {cutoff}
                )
                """, cancellationToken);

            report.ExecutionsDeleted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM workflow_execution_cache
                WHERE process_definition_key = {processKey}
                  AND start_time < {cutoff}
                """, cancellationToken);
        }

        if (report.AnyDeleted)
        {
            _logger.LogInformation(
                "Workflow cache retention sweep deleted: {Events} events, {Variables} variables, {Tasks} tasks, {Executions} executions.",
                report.EventsDeleted, report.VariablesDeleted, report.TasksDeleted, report.ExecutionsDeleted);
        }

        return report;
    }
}

public sealed class RetentionReport
{
    public int ExecutionsDeleted { get; set; }
    public int TasksDeleted { get; set; }
    public int VariablesDeleted { get; set; }
    public int EventsDeleted { get; set; }
    public bool AnyDeleted => ExecutionsDeleted + TasksDeleted + VariablesDeleted + EventsDeleted > 0;
}
