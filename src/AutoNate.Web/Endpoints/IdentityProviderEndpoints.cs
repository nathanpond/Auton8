using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Identity;

namespace AutoNate.Web.Endpoints;

/// <summary>
/// Admin CRUD for identity providers, plus the pre-flight configuration test.
/// </summary>
/// <remarks>
/// Every route carries an explicit (kind, action) gate — project invariant 3,
/// enforced by AuthorizationGatePresenceTests and KindGateEnforcementTests.
///
/// No route returns a provider secret. The store's DTO has nowhere to put one,
/// which is the structural version of that promise rather than a convention
/// each handler has to remember.
/// </remarks>
public static class IdentityProviderEndpoints
{
    public static IEndpointRouteBuilder MapIdentityProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/identity-providers").RequireAuthorization();

        group.MapGet("/", async (IIdentityProviderStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(ct)))
            .RequireKindPermission(EntityKinds.IdentityProvider, Actions.View);

        group.MapGet("/{id:guid}", async (Guid id, IIdentityProviderStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequireKindPermission(EntityKinds.IdentityProvider, Actions.View);

        group.MapPost("/", async (
            CreateIdentityProviderRequest request,
            HttpContext http,
            IIdentityProviderStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            try
            {
                var row = await store.CreateAsync(request, actorId, ct);
                return Results.Created($"/api/admin/identity-providers/{row.Id}", row);
            }
            catch (IdentityProviderValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.IdentityProvider, Actions.Create);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateIdentityProviderRequest request,
            HttpContext http,
            IIdentityProviderStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            try
            {
                var row = await store.UpdateAsync(id, request, actorId, ct);
                return row is null ? Results.NotFound() : Results.Ok(row);
            }
            catch (IdentityProviderValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.IdentityProvider, Actions.Edit);

        // Enable and disable are their own routes rather than a PATCH field.
        // Turning a provider on changes who can reach the system, and it should
        // be a distinct thing to grant, log and read in an audit trail — not a
        // boolean buried in an edit payload.
        group.MapPost("/{id:guid}/enable", async (
            Guid id, HttpContext http, IIdentityProviderStore store, CancellationToken ct) =>
                await SetEnabled(id, true, http, store, ct))
            .RequireKindPermission(EntityKinds.IdentityProvider, Actions.Edit);

        group.MapPost("/{id:guid}/disable", async (
            Guid id, HttpContext http, IIdentityProviderStore store, CancellationToken ct) =>
                await SetEnabled(id, false, http, store, ct))
            .RequireKindPermission(EntityKinds.IdentityProvider, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext http, IIdentityProviderStore store, CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            return await store.DeleteAsync(id, actorId, ct) ? Results.NoContent() : Results.NotFound();
        }).RequireKindPermission(EntityKinds.IdentityProvider, Actions.Delete);

        // Pre-flight: reach the provider's discovery or metadata endpoint and
        // report what came back, so a typo in an issuer URL is caught when the
        // provider is saved rather than at someone's first sign-in attempt.
        group.MapPost("/{id:guid}/test", async (
            Guid id,
            IIdentityProviderStore store,
            IIdentityProviderConfigurationTester tester,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            return Results.Ok(await tester.TestAsync(row, ct));
        }).RequireKindPermission(EntityKinds.IdentityProvider, Actions.Edit);

        return app;
    }

    private static async Task<IResult> SetEnabled(
        Guid id, bool enabled, HttpContext http, IIdentityProviderStore store, CancellationToken ct)
    {
        var actorId = http.GetActorId();
        if (actorId == Guid.Empty) return Results.Unauthorized();
        var row = await store.SetEnabledAsync(id, enabled, actorId, ct);
        return row is null ? Results.NotFound() : Results.Ok(row);
    }
}
