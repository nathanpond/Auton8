using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

public static class RoleAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapRoleAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/role-assignments").RequireAuthorization();

        group.MapDelete("/{id:guid}", async (
            Guid id, IRoleAssignmentStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var ok = await store.RevokeAsync(id, ct);
            if (!ok) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.RoleAssignmentRevoked,
                IamResourceKinds.RoleAssignment,
                resource: new { id },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        // Look up assignments for a principal — useful for "show all roles for this user"
        group.MapGet("/by-principal", async (
            string principalKind,
            string principalId,
            IRoleAssignmentStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
            {
                var assignments = await store.ListForPrincipalAsync(principalKind, principalId, ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.RoleAssignmentsByPrincipalViewed,
                    IamResourceKinds.RoleAssignment,
                    resource: new { principalKind, principalId },
                    details: new { resultCount = assignments.Count },
                    ct);
                return Results.Ok(assignments);
            });

        return app;
    }
}
