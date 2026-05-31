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
            return Results.Ok(new PipelineRunDetailDto(run, steps));
        }).RequirePermission(EntityKinds.Pipeline, Actions.View);

        return app;
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
    IReadOnlyList<AutoNate.Web.Persistence.Scaffolded.PipelineRunStep> Steps);
