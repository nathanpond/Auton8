using AutoNate.Web.Services.Authorization;

namespace AutoNate.Web.Endpoints;

public static class RoleAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapRoleAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/role-assignments").RequireAuthorization();

        group.MapDelete("/{id:guid}", async (
            Guid id, IRoleAssignmentStore store, CancellationToken ct) =>
        {
            var ok = await store.RevokeAsync(id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        // Look up assignments for a principal — useful for "show all roles for this user"
        group.MapGet("/by-principal", async (
            string principalKind,
            string principalId,
            IRoleAssignmentStore store,
            CancellationToken ct) =>
                Results.Ok(await store.ListForPrincipalAsync(principalKind, principalId, ct)));

        return app;
    }
}
