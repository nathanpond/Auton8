using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Pipelines.Orchestration;

// Polls for Queued pipeline runs at a short interval and dispatches them to
// the orchestrator. Same shape as the dataset-refresh scheduler: a v1
// BackgroundService keeps the surface small; modeling this as an
// IProjection<PipelineId> for pause/resume + health UI is a Phase 5.1
// follow-up.
public sealed class PipelineRunWorker(
    IServiceProvider services,
    ILogger<PipelineRunWorker> log) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int MaxBatch = 4;

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
                log.LogError(ex, "PipelineRunWorker tick failed; swallowing to keep the loop alive.");
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
        var runStore = scope.ServiceProvider.GetRequiredService<IPipelineRunStore>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<PipelineOrchestrator>();
        var due = await runStore.DequeueOldestAsync(MaxBatch, ct);
        if (due.Count == 0) return;
        foreach (var run in due)
        {
            try
            {
                await orchestrator.RunAsync(run.Id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Pipeline run {RunId} failed in worker; continuing tick.", run.Id);
            }
        }
    }
}
