using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models.Forms;
using AutoNate.Web.Services.Forms;

namespace AutoNate.Web.Endpoints;

public static class FormEndpoints
{
    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/forms").RequireAuthorization();

        group.MapGet("/", async (
            IFormStore store,
            CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.Form, Actions.View);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IFormStore store,
            CancellationToken ct) =>
        {
            var form = await store.GetAsync(id, ct);
            return form is null ? Results.NotFound() : Results.Ok(form);
        }).RequirePermission(EntityKinds.Form, Actions.View);

        group.MapPost("/", async (
            CreateFormRequest request,
            HttpContext http,
            IFormStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var form = await store.CreateAsync(request, actorId, ct);
                return Results.Created($"/api/forms/{form.Id}", form);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.Form, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            SaveFormRequest request,
            HttpContext http,
            IFormStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var form = await store.SaveAsync(id, request, actorId, ct);
                return Results.Ok(form);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.Form, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IFormStore store,
            CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.Form, Actions.Delete);

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            HttpContext http,
            IFormStore store,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var form = await store.PublishAsync(id, actorId, ct);
                return Results.Ok(form);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequirePermission(EntityKinds.Form, Actions.Publish)
          .DisableAntiforgery();

        group.MapGet("/{id:guid}/versions", async (
            Guid id,
            IFormStore store,
            CancellationToken ct) =>
        {
            var versions = await store.ListVersionsAsync(id, ct);
            return Results.Ok(versions);
        }).RequirePermission(EntityKinds.Form, Actions.View);

        group.MapPost("/{id:guid}/restore/{versionNumber:int}", async (
            Guid id,
            int versionNumber,
            HttpContext http,
            IFormStore store,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var form = await store.RestoreAsync(id, versionNumber, actorId, ct);
            return form is null ? Results.NotFound() : Results.Ok(form);
        }).RequirePermission(EntityKinds.Form, Actions.Edit)
          .DisableAntiforgery();

        // Polling endpoint for `/formdev/{shortCode}` — returns the current
        // draft snapshot. Gated kind-wide on Form.View so any user authorized
        // to see forms can hot-reload the dev preview tab.
        group.MapGet("/dev/{shortCode}", async (
            string shortCode,
            IFormStore store,
            CancellationToken ct) =>
        {
            var snapshot = await store.GetDraftSnapshotByShortCodeAsync(shortCode, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        }).RequireKindPermission(EntityKinds.Form, Actions.View);

        // Render endpoint for `/form/{shortCode}` — returns the
        // currently-published version, or 404 when not published or
        // site_available=false. Available to any authenticated user; no
        // per-form Form.View permission is required (path inherits the
        // parent group's RequireAuthorization). The `/public/` segment in
        // the route is a legacy name and does NOT mean anonymous access.
        group.MapGet("/public/{shortCode}", async (
            string shortCode,
            IFormStore store,
            CancellationToken ct) =>
        {
            var snapshot = await store.GetPublishedSnapshotByShortCodeAsync(shortCode, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        }).OpenToAuthenticated("runtime form render for any signed-in user; site_available=false yields 404. The store filters drafts and unpublished forms.");

        return app;
    }
}
