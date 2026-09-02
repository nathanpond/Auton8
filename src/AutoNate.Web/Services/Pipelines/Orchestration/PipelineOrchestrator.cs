using System.Security.Claims;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Pipelines.Execution;
using AutoNate.Web.Services.Query;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Pipelines.Orchestration;

// In-process DAG executor for Phase 5 of the Data Stores plan. Walks the
// topologically-sorted graph, threads materialised DataFrames through node
// runners by upstream-id, persists per-step status/timings/row counts.
//
// The orchestrator runs synchronously inside whatever caller invokes it
// (PipelineRunWorker most of the time; future API "synchronous test run"
// callers similarly). Phase 6 will fan execution out across K8s jobs via
// the same orchestrator contract — by then the per-node materialise step
// becomes a JetStream payload publish.
public sealed class PipelineOrchestrator(
    IPipelineStore pipelineStore,
    IPipelineRunStore runStore,
    INodeRunnerRegistry runnerRegistry,
    IShareIssuerPrincipalFactory principalFactory,
    ILogger<PipelineOrchestrator> log)
{
    public async Task RunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await runStore.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            log.LogWarning("Pipeline run {RunId} disappeared before execution.", runId);
            return;
        }
        if (run.Status != PipelineRunStatuses.Queued) return;

        await runStore.MarkRunningAsync(runId, DateTime.UtcNow, cancellationToken);
        var graph = PipelineGraph.FromJson(run.GraphSnapshotJson);

        IReadOnlyList<PipelineNode> ordered;
        try
        {
            ordered = PipelineGraphValidator.TopologicalSort(graph);
        }
        catch (PipelineGraphValidationException ex)
        {
            await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Failed, ex.Message, DateTime.UtcNow, cancellationToken);
            return;
        }
        if (ordered.Count == 0)
        {
            await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Succeeded, null, DateTime.UtcNow, cancellationToken);
            return;
        }

        // Build the issuer principal once — pipeline runs execute as the
        // triggering actor so the dataset/source grants the actor already
        // has apply throughout the graph.
        var actor = await principalFactory.BuildAsync(run.TriggeredBy, cancellationToken)
            ?? new ClaimsPrincipal(new ClaimsIdentity());

        var upstreamMap = PipelineGraphValidator.ResolveUpstreamMap(graph);
        var outputs = new Dictionary<string, DataFrame?>(StringComparer.Ordinal);

        try
        {
            foreach (var node in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // External cancel — POST /api/pipelines/{id}/runs/{runId}/cancel
                // flips the row to Cancelled. We re-read the row at the top of
                // each iteration so a mid-DAG cancel takes effect at the next
                // node boundary. The currently-running node (if any) is the
                // previous loop body, already completed by now.
                var current = await runStore.GetAsync(runId, cancellationToken);
                if (current?.Status == PipelineRunStatuses.Cancelled)
                {
                    log.LogInformation("Pipeline run {RunId} was cancelled externally; aborting before node {NodeId}.",
                        runId, node.Id);
                    return;
                }
                var stepRow = await runStore.CreateStepAsync(runId, node.Id, node.Kind, cancellationToken);
                await runStore.MarkStepStartedAsync(stepRow.Id, DateTime.UtcNow, cancellationToken);
                // Audit fix archived-11 — per-step log buffer. Orchestrator captures
                // boundary entries at start / success / fail / cancel; the
                // SPA renders these inline below the step row so users see
                // more than just status + rowCount + errorMessage on a
                // failed run.
                var stepStart = DateTime.UtcNow;
                var stepLogs = new List<PipelineRunStepLog>();
                if (!runnerRegistry.TryGet(node.Kind, out var runner))
                {
                    var err = $"No runner registered for node kind '{node.Kind}'.";
                    stepLogs.Add(new PipelineRunStepLog(DateTime.UtcNow, "error", err));
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Failed, null, err, DateTime.UtcNow, stepLogs, cancellationToken);
                    await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Failed, err, DateTime.UtcNow, cancellationToken);
                    return;
                }
                var inputFrames = upstreamMap[node.Id]
                    .Select(upId => outputs.GetValueOrDefault(upId))
                    .Where(f => f is not null)
                    .Select(f => f!)
                    .ToList();
                var inputRowsTotal = inputFrames.Sum(f => (long)f.Rows.Count);
                stepLogs.Add(new PipelineRunStepLog(stepStart, "info",
                    $"Starting node '{node.Id}' (kind={node.Kind}, key={node.Key}) with {inputFrames.Count} input frame(s), {inputRowsTotal} rows total."));
                try
                {
                    var output = await runner.RunAsync(
                        new NodeRunnerContext(node, inputFrames, actor, runId), cancellationToken);
                    outputs[node.Id] = output;
                    var rowCount = output?.Rows.Count is { } rc ? (long?)rc : null;
                    var elapsedMs = (long)(DateTime.UtcNow - stepStart).TotalMilliseconds;
                    stepLogs.Add(new PipelineRunStepLog(DateTime.UtcNow, "info",
                        $"Succeeded in {elapsedMs} ms; output rows = {rowCount?.ToString() ?? "—"}."));
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Succeeded, rowCount, null, DateTime.UtcNow, stepLogs, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    stepLogs.Add(new PipelineRunStepLog(DateTime.UtcNow, "warn",
                        "Cancelled mid-execution by worker shutdown."));
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Cancelled, null, "Cancelled.", DateTime.UtcNow, stepLogs, cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    // Capture the exception type + message + a clipped stack
                    // trace — the inline error_message still gets the short
                    // form, but the log entry preserves enough detail to
                    // debug from the SPA without server-log access.
                    var stack = ex.StackTrace ?? string.Empty;
                    var clippedStack = string.Join('\n',
                        stack.Split('\n').Take(10));
                    stepLogs.Add(new PipelineRunStepLog(DateTime.UtcNow, "error",
                        $"{ex.GetType().Name}: {ex.Message}\n{clippedStack}"));
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Failed, null, ex.Message, DateTime.UtcNow, stepLogs, cancellationToken);
                    await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Failed, ex.Message, DateTime.UtcNow, cancellationToken);
                    log.LogWarning(ex, "Pipeline run {RunId} failed at node {NodeId}.", runId, node.Id);
                    return;
                }
            }
            await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Succeeded, null, DateTime.UtcNow, cancellationToken);
            await pipelineStore.MarkRunCompletedAsync(run.PipelineId, DateTime.UtcNow, cancellationToken);
            log.LogInformation("Pipeline run {RunId} succeeded ({NodeCount} nodes).", runId, ordered.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Cancelled, "Cancelled.", DateTime.UtcNow, CancellationToken.None);
            throw;
        }
    }
}
