using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Authorization;

namespace AutoNate.Web.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/roles").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IRoleStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAuthorizedAsync(http.User, ct)));

        group.MapGet("/{id:guid}", async (Guid id, IRoleStore store, CancellationToken ct) =>
        {
            var role = await store.GetAsync(id, ct);
            return role is null ? Results.NotFound() : Results.Ok(role);
        }).RequirePermission(EntityKinds.Role, Actions.View);

        group.MapPost("/", async (
            CreateRoleRequest request,
            HttpContext http,
            IRoleStore store,
            CancellationToken ct) =>
        {
            try
            {
                var role = await store.CreateAsync(
                    new CreateRoleInput(request.Name, request.Description),
                    ActorId(http), ct);
                return Results.Created($"/api/admin/roles/{role.Id}", role);
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.Role, Actions.Create);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateRoleRequest request,
            HttpContext http,
            IRoleStore store,
            CancellationToken ct) =>
        {
            try
            {
                var role = await store.UpdateAsync(
                    id, new UpdateRoleInput(request.Name, request.Description),
                    ActorId(http), ct);
                return Results.Ok(role);
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Role, Actions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IRoleStore store, CancellationToken ct) =>
        {
            try
            {
                var deleted = await store.DeleteAsync(id, ct);
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Role, Actions.Delete);

        // Role permissions are now grants in permission_grants with
        // principal_kind='role'. Manage them via /api/admin/grants.

        // Assignments scoped under a role
        group.MapGet("/{id:guid}/assignments", async (
            Guid id, IRoleAssignmentStore store, CancellationToken ct) =>
                Results.Ok(await store.ListByRoleAsync(id, ct)))
            .RequirePermission(EntityKinds.Role, Actions.View);

        group.MapPost("/{id:guid}/assignments", async (
            Guid id,
            CreateAssignmentRequest request,
            HttpContext http,
            IRoleAssignmentStore store,
            CancellationToken ct) =>
        {
            try
            {
                var assignment = await store.AssignAsync(
                    new CreateRoleAssignmentInput(
                        id, request.PrincipalKind, request.PrincipalId, request.ScopeString),
                    ActorId(http), ct);
                return Results.Created($"/api/admin/role-assignments/{assignment.Id}", assignment);
            }
            catch (RoleAssignmentValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Role, Actions.Assign);

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public sealed record CreateRoleRequest(string Name, string? Description);
    public sealed record UpdateRoleRequest(string? Name, string? Description);
    public sealed record CreateAssignmentRequest(
        string PrincipalKind, string PrincipalId, string? ScopeString);
}
