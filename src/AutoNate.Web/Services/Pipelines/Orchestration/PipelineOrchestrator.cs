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
                var stepRow = await runStore.CreateStepAsync(runId, node.Id, node.Kind, cancellationToken);
                await runStore.MarkStepStartedAsync(stepRow.Id, DateTime.UtcNow, cancellationToken);
                if (!runnerRegistry.TryGet(node.Kind, out var runner))
                {
                    var err = $"No runner registered for node kind '{node.Kind}'.";
                    await runStore.MarkStepCompletedAsync(stepRow.Id, PipelineRunStatuses.Failed, null, err, DateTime.UtcNow, cancellationToken);
                    await runStore.MarkCompletedAsync(runId, PipelineRunStatuses.Failed, err, DateTime.UtcNow, cancellationToken);
                    return;
                }
                var inputFrames = upstreamMap[node.Id]
                    .Select(upId => outputs.GetValueOrDefault(upId))
                    .Where(f => f is not null)
                    .Select(f => f!)
                    .ToList();
                try
                {
                    var output = await runner.RunAsync(
                        new NodeRunnerContext(node, inputFrames, actor, runId), cancellationToken);
                    outputs[node.Id] = output;
                    var rowCount = output?.Rows.Count is { } rc ? (long?)rc : null;
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Succeeded, rowCount, null, DateTime.UtcNow, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Cancelled, null, "Cancelled.", DateTime.UtcNow, cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    await runStore.MarkStepCompletedAsync(
                        stepRow.Id, PipelineRunStatuses.Failed, null, ex.Message, DateTime.UtcNow, cancellationToken);
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
