using System.Security.Claims;
using AutoNate.Web.Services.Authorization;

namespace AutoNate.Web.Endpoints;

public static class PermissionGrantEndpoints
{
    public static IEndpointRouteBuilder MapPermissionGrantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/grants").RequireAuthorization();

        group.MapGet("/", async (
            string? principalKind,
            string? principalId,
            IPermissionGrantStore store,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(principalKind) && !string.IsNullOrEmpty(principalId))
            {
                return Results.Ok(await store.ListForPrincipalAsync(principalKind, principalId, ct));
            }

            return Results.Ok(await store.ListAsync(ct));
        });

        group.MapPost("/", async (
            CreateGrantRequest request,
            HttpContext http,
            IPermissionGrantStore store,
            CancellationToken ct) =>
        {
            try
            {
                var grant = await store.CreateAsync(
                    new CreatePermissionGrantInput(
                        request.PrincipalKind,
                        request.PrincipalId,
                        request.Action,
                        request.SelectorString,
                        request.Effect,
                        request.Priority),
                    ActorId(http), ct);
                return Results.Created($"/api/admin/grants/{grant.Id}", grant);
            }
            catch (PermissionGrantValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (Guid id, IPermissionGrantStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public sealed record CreateGrantRequest(
        string PrincipalKind,
        string PrincipalId,
        string Action,
        string SelectorString,
        string Effect,
        int Priority);
}
