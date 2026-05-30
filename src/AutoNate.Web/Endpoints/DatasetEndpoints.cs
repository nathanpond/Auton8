using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Cached;

namespace AutoNate.Web.Endpoints;

// Dataset CRUD + manual refresh (Phase 2 of the Data Stores plan).
// Querying datasets is the AQL surface — `FROM Dataset("name")` — and is
// served by AqlExecuteEndpoint; nothing here returns row data directly.
public static class DatasetEndpoints
{
    public static IEndpointRouteBuilder MapDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasets").RequireAuthorization();

        group.MapGet("/", async (IDatasetStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.Dataset, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, IDatasetStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.Dataset, Actions.View);

        group.MapPost("/", async (
            CreateDatasetRequest request,
            HttpContext http,
            IDatasetStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (!Enum.TryParse<DatasetMode>(request.Mode, ignoreCase: true, out var mode))
            {
                return Results.BadRequest(new { reason = $"Unknown dataset mode '{request.Mode}'." });
            }
            if (request.Columns is null || request.Columns.Count == 0)
            {
                return Results.BadRequest(new { reason = "At least one column is required." });
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreateDatasetInput(
                        request.Name,
                        request.Description,
                        mode,
                        request.Columns,
                        request.SourceKind,
                        request.SourceId,
                        request.SourceTableName,
                        request.RefreshCron),
                    actorId, ct);
                return Results.Created($"/api/datasets/{row.Id}", row);
            }
            catch (DatasetNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.Dataset, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDatasetRequest request,
            HttpContext http,
            IDatasetStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateDatasetInput(request.Name, request.Description, request.RefreshCron),
                    actorId, ct);
                return Results.Ok(row);
            }
            catch (DatasetNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DatasetNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.Dataset, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id, IDatasetStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.Dataset, Actions.Delete);

        // Manual refresh: invokes the materializer synchronously. Scheduled
        // refresh runs from DatasetRefreshScheduler at one-minute granularity.
        group.MapPost("/{id:guid}/refresh", async (
            Guid id,
            ICachedDatasetMaterializer materializer,
            CancellationToken ct) =>
        {
            try
            {
                await materializer.RefreshAsync(id, ct);
                return Results.NoContent();
            }
            catch (DatasetNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.Dataset, Actions.Refresh);

        return app;
    }
}

public sealed record class CreateDatasetRequest(
    string Name,
    string? Description,
    string Mode,
    IReadOnlyList<DatasetColumn> Columns,
    string SourceKind,
    Guid SourceId,
    string? SourceTableName,
    string? RefreshCron);

public sealed record class UpdateDatasetRequest(
    string? Name,
    string? Description,
    string? RefreshCron);
