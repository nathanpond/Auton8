using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.DataConnectors;

namespace AutoNate.Web.Endpoints;

public static class DataConnectorEndpoints
{
    public static IEndpointRouteBuilder MapDataConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataconnectors").RequireAuthorization();

        group.MapGet("/", async (IDataConnectorStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.DataConnector, Actions.List);

        // Live list of registered connector kinds (built-in + plugin-contributed).
        // Surfaced so the SPA create form's kind dropdown stays in sync with what
        // plugins enable/disable at runtime.
        group.MapGet("/kinds", (IDataConnectorHandlerRegistry registry) =>
        {
            return Results.Ok(registry.Kinds);
        }).RequireKindPermission(EntityKinds.DataConnector, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, IDataConnectorStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.DataConnector, Actions.View);

        group.MapPost("/", async (
            CreateDataConnectorRequest request,
            HttpContext http,
            IDataConnectorStore store,
            IDataConnectorHandlerRegistry registry,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (!registry.TryGet(request.Kind, out _))
            {
                return Results.BadRequest(new { reason = $"Unknown connector kind '{request.Kind}'." });
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreateDataConnectorInput(request.Name, request.Description, request.Kind, request.ConfigJson ?? "{}"),
                    actorId, ct);
                return Results.Created($"/api/dataconnectors/{row.Id}", row);
            }
            catch (DataConnectorNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.DataConnector, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDataConnectorRequest request,
            HttpContext http,
            IDataConnectorStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateDataConnectorInput(request.Name, request.Description, request.ConfigJson),
                    actorId, ct);
                return Results.Ok(row);
            }
            catch (DataConnectorNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DataConnectorNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataConnector, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id, IDataConnectorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.DataConnector, Actions.Delete);

        group.MapPost("/{id:guid}/test", async (
            Guid id,
            IDataConnectorStore store,
            IDataConnectorHandlerRegistry registry,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            if (!registry.TryGet(row.Kind, out var handler))
            {
                return Results.BadRequest(new { reason = $"No handler registered for kind '{row.Kind}'." });
            }
            var result = await handler.TestAsync(row, ct);
            return Results.Ok(result);
        }).RequirePermission(EntityKinds.DataConnector, Actions.Connect);

        return app;
    }
}

public sealed record class CreateDataConnectorRequest(
    string Name, string? Description, string Kind, string? ConfigJson);

public sealed record class UpdateDataConnectorRequest(
    string? Name, string? Description, string? ConfigJson);
