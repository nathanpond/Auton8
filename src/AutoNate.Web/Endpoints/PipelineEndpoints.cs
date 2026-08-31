using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Pipelines;

namespace AutoNate.Web.Endpoints;

// Pipeline CRUD + enqueue + run history (Phase 5 of the Data Stores plan).
// Runs are queued in `pipeline_runs` and drained by PipelineRunWorker; the
// endpoint returns the new run row immediately rather than blocking for
// orchestration to finish.
public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pipelines").RequireAuthorization();

        group.MapGet("/", async (IPipelineStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.Pipeline, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, IPipelineStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.Pipeline, Actions.View);

        group.MapPost("/", async (
            CreatePipelineRequest request,
            HttpContext http,
            IPipelineStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var graph = request.Graph ?? PipelineGraph.Empty;
            try { PipelineGraphValidator.TopologicalSort(graph); }
            catch (PipelineGraphValidationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreatePipelineInput(request.Name, request.Description, graph, request.ScheduleCron),
                    actorId, ct);
                return Results.Created($"/api/pipelines/{row.Id}", row);
            }
            catch (PipelineNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.Pipeline, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdatePipelineRequest request,
            HttpContext http,
            IPipelineStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (request.Graph is not null)
            {
                try { PipelineGraphValidator.TopologicalSort(request.Graph); }
                catch (PipelineGraphValidationException ex)
                {
                    return Results.BadRequest(new { reason = ex.Message });
                }
            }
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdatePipelineInput(request.Name, request.Description, request.Graph, request.ScheduleCron),
                    actorId, ct);
                return Results.Ok(row);
            }
            catch (PipelineNotFoundException) { return Results.NotFound(); }
            catch (PipelineNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.Pipeline, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id, IPipelineStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.Pipeline, Actions.Delete);

        // Enqueue a manual run. The orchestrator runs out-of-band in
        // PipelineRunWorker; the endpoint returns the queued row id so the
        // SPA can poll for completion via /runs/{runId}.
        group.MapPost("/{id:guid}/run", async (
            Guid id,
            HttpContext http,
            IPipelineStore store,
            IPipelineRunStore runStore,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var pipeline = await store.GetAsync(id, ct);
            if (pipeline is null) return Results.NotFound();
            var run = await runStore.EnqueueAsync(
                id, pipeline.GraphJson, actorId, PipelineRunTriggerKinds.Manual, ct);
            return Results.Accepted($"/api/pipelines/{id}/runs/{run.Id}", run);
        }).RequirePermission(EntityKinds.Pipeline, Actions.Run)
          .DisableAntiforgery();

        group.MapGet("/{id:guid}/runs", async (
            Guid id, IPipelineRunStore runStore, CancellationToken ct) =>
        {
            var rows = await runStore.ListForPipelineAsync(id, limit: 50, ct);
            return Results.Ok(rows);
        }).RequirePermission(EntityKinds.Pipeline, Actions.View);

        group.MapGet("/{id:guid}/runs/{runId:guid}", async (
            Guid id, Guid runId, IPipelineRunStore runStore, CancellationToken ct) =>
        {
            var run = await runStore.GetAsync(runId, ct);
            if (run is null || run.PipelineId != id) return Results.NotFound();
            var steps = await runStore.ListStepsAsync(runId, ct);
            // Parse logs_json server-side so the SPA gets a typed array
            // rather than a stringly-encoded one. A hand-corrupted row
            // falls back to an empty list (the schema's default) so a
            // single bad row doesn't fail the whole detail call.
            var stepDtos = steps.Select(s => new PipelineRunStepDto(
                s.Id, s.PipelineRunId, s.NodeKey, s.NodeKind, s.Status,
                s.StartedAtUtc, s.CompletedAtUtc, s.RowCount, s.ErrorMessage,
                PipelineEndpointHelpers.SafeParseLogs(s.LogsJson))).ToList();
            return Results.Ok(new PipelineRunDetailDto(run, stepDtos));
        }).RequirePermission(EntityKinds.Pipeline, Actions.View);

        // Audit fix #10 — cancel a Queued or Running run. The store flips
        // the row to Cancelled and the orchestrator's between-node check
        // bails on the next iteration; the worker won't pick up a
        // Queued row that's been flipped. Already-terminal runs return
        // 409 so the SPA can render a "too late" toast instead of a
        // silent no-op. Same Run permission as enqueue (cancelling is
        // the inverse of starting; conceptually one permission).
        group.MapPost("/{id:guid}/runs/{runId:guid}/cancel", async (
            Guid id, Guid runId, IPipelineRunStore runStore, CancellationToken ct) =>
        {
            var run = await runStore.GetAsync(runId, ct);
            if (run is null || run.PipelineId != id) return Results.NotFound();
            var result = await runStore.RequestCancellationAsync(runId, DateTime.UtcNow, ct);
            return result switch
            {
                RunCancellationResult.NotFound => Results.NotFound(),
                RunCancellationResult.AlreadyTerminal => Results.Conflict(
                    new { reason = $"Run is already {run.Status}; nothing to cancel." }),
                _ => Results.NoContent(),
            };
        // Cancel, not Run: an admin granting pipeline:cancel to an on-call
        // operator expects them to be able to stop a run without also being
        // able to start one. This was gated on Run, so the advertised grant
        // 403'd and the only way to cancel was the grant that also starts
        // runs (#24). Deployments that relied on run-implies-cancel need the
        // cancel grant added.
        }).RequirePermission(EntityKinds.Pipeline, Actions.Cancel)
          .DisableAntiforgery();

        // Audit fix #10 — retry a Failed or Cancelled run. Enqueues a new
        // run with the original run's graph snapshot (so a retry
        // exercises the same DAG even if the pipeline's saved graph has
        // since changed); trigger kind is `manual` because the human is
        // explicitly asking. Already-running / queued / succeeded runs
        // 409 — retry is only valid for terminal non-success states.
        group.MapPost("/{id:guid}/runs/{runId:guid}/retry", async (
            Guid id,
            Guid runId,
            HttpContext http,
            IPipelineRunStore runStore,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var run = await runStore.GetAsync(runId, ct);
            if (run is null || run.PipelineId != id) return Results.NotFound();
            if (run.Status != PipelineRunStatuses.Failed
                && run.Status != PipelineRunStatuses.Cancelled)
            {
                return Results.Conflict(new
                {
                    reason = $"Run is {run.Status}; only Failed or Cancelled runs can be retried."
                });
            }
            var fresh = await runStore.EnqueueAsync(
                id, run.GraphSnapshotJson, actorId, PipelineRunTriggerKinds.Manual, ct);
            return Results.Accepted($"/api/pipelines/{id}/runs/{fresh.Id}", fresh);
        }).RequirePermission(EntityKinds.Pipeline, Actions.Run)
          .DisableAntiforgery();

        return app;
    }
}

file static class PipelineEndpointHelpers
{
    public static IReadOnlyList<AutoNate.Web.Services.Pipelines.PipelineRunStepLog> SafeParseLogs(string? logsJson)
    {
        if (string.IsNullOrWhiteSpace(logsJson)) return Array.Empty<AutoNate.Web.Services.Pipelines.PipelineRunStepLog>();
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<AutoNate.Web.Services.Pipelines.PipelineRunStepLog>>(logsJson);
            return parsed ?? new();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<AutoNate.Web.Services.Pipelines.PipelineRunStepLog>();
        }
    }
}

public sealed record class CreatePipelineRequest(
    string Name,
    string? Description,
    PipelineGraph? Graph,
    string? ScheduleCron);

public sealed record class UpdatePipelineRequest(
    string? Name,
    string? Description,
    PipelineGraph? Graph,
    string? ScheduleCron);

public sealed record class PipelineRunDetailDto(
    AutoNate.Web.Persistence.Scaffolded.PipelineRun Run,
    IReadOnlyList<PipelineRunStepDto> Steps);

public sealed record class PipelineRunStepDto(
    Guid Id,
    Guid PipelineRunId,
    string NodeKey,
    string NodeKind,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? RowCount,
    string? ErrorMessage,
    IReadOnlyList<AutoNate.Web.Services.Pipelines.PipelineRunStepLog> Logs);
