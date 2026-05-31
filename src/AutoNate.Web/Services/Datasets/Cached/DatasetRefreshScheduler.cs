using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Datasets.Cached;

// Polls the datasets table once per minute and refreshes any Cached dataset
// whose RefreshCron schedule indicates the next due-time has elapsed
// (docs/plans/2026-05-30-data-stores-implementation.md Phase 2). Manual
// refresh is a direct synchronous CachedDatasetMaterializer.RefreshAsync
// call from the endpoint — no queue plumbing in v1. Future work: model
// this as IProjection&lt;DatasetId&gt; so admin pause/resume + projection-health
// UI applies; the plan calls out that as the canonical path.
//
// Cron parsing is intentionally narrow for v1: we recognize a five-field
// expression and treat "missing or invalid" as "manual only" rather than
// throwing. Full cron-vocabulary support (named lists, ranges, step values)
// lands when the projection-framework integration arrives.
public sealed class DatasetRefreshScheduler(
    IServiceProvider services,
    ILogger<DatasetRefreshScheduler> log) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "DatasetRefreshScheduler tick failed; swallowing to keep the loop alive.");
            }
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var due = await db.Datasets.AsNoTracking()
            .Where(d => d.Mode == (short)DatasetMode.Cached && d.RefreshCron != null)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        var now = DateTime.UtcNow;
        var materializer = scope.ServiceProvider.GetRequiredService<ICachedDatasetMaterializer>();
        foreach (var d in due)
        {
            if (!IsDue(d, now)) continue;
            try
            {
                await materializer.RefreshAsync(d.Id, ct);
            }
            catch (Exception ex)
            {
                // Best-effort: log and continue. The next tick re-attempts.
                log.LogWarning(ex,
                    "Scheduled refresh of dataset {Id} ({Name}) failed.",
                    d.Id, d.Name);
            }
        }
    }

    // Minimal cron-due check: parses the cron interval bound as "every N
    // minutes" via the common `*/N * * * *` form. Anything else is treated
    // as "manual only" until the full cron parser lands with the
    // IProjection integration.
    private static bool IsDue(Dataset dataset, DateTime nowUtc)
    {
        if (dataset.RefreshCron is null) return false;
        var trimmed = dataset.RefreshCron.Trim();
        if (TryParseMinutesInterval(trimmed, out var minutes))
        {
            if (dataset.LastRefreshedAtUtc is null) return true;
            return nowUtc - dataset.LastRefreshedAtUtc.Value >= TimeSpan.FromMinutes(minutes);
        }
        return false;
    }

    private static bool TryParseMinutesInterval(string cron, out int minutes)
    {
        minutes = 0;
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;
        if (parts[1] != "*" || parts[2] != "*" || parts[3] != "*" || parts[4] != "*") return false;
        var minute = parts[0];
        if (minute == "*")
        {
            minutes = 1;
            return true;
        }
        if (minute.StartsWith("*/", StringComparison.Ordinal) && int.TryParse(minute[2..], out var n) && n > 0)
        {
            minutes = n;
            return true;
        }
        return false;
    }
}
