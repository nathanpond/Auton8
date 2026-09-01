using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

public static class RoleAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapRoleAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/role-assignments").RequireAuthorization();

        // Authorized in the handler because the gate has to be about the role
        // this assignment names, and the route only carries the assignment id
        // (#182).
        //
        // It was gated with RequireKindPermission(Role, Assign), which asks
        // only "does any allow grant for role+assign exist?" — it never
        // resolves the assignment, so it could not tell one role from
        // another. A grant scoped to a single throwaway role was therefore
        // enough to revoke anybody's membership of any role, SuperAdmin
        // included: narrow enough to hand out one role, wide enough to strip
        // every role in the system. The assign side (POST
        // /api/admin/roles/{id}/assignments) was already instance-level, so
        // the two halves of one privilege disagreed.
        //
        // Resolving before authorizing also means an unknown id answers 404
        // for a caller who could have revoked it and 403 for one who could
        // not — the denial does not double as an existence oracle.
        group.MapDelete("/{id:guid}", async (
            Guid id, IRoleAssignmentStore store,
            IAuthorizer authorizer, HttpContext http,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var assignment = await store.GetAsync(id, ct);

            var decision = await authorizer.AuthorizeAsync(
                http.User,
                Actions.Assign,
                new EntityRef(EntityKinds.Role, (assignment?.RoleId ?? Guid.Empty).ToString()),
                ct);
            if (!decision.IsAllowed)
            {
                await auditPublisher.PublishAsync(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.AccessDenied,
                    AuthEventTopic.ResourceKind,
                    resource: new
                    {
                        kind = EntityKinds.Role,
                        id = assignment?.RoleId.ToString(),
                        action = Actions.Assign
                    },
                    details: new { reason = decision.Reason, assignmentId = id },
                    ct);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (assignment is null) return Results.NotFound();

            var ok = await store.RevokeAsync(id, ct);
            if (!ok) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.RoleAssignmentRevoked,
                IamResourceKinds.RoleAssignment,
                resource: new { id, roleId = assignment.RoleId },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "resolves the assignment and authorizes (Role, Assign) against the role it names; "
              + "a kind-level gate cannot distinguish one role from another");

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
            }).RequireKindPermission(EntityKinds.Role, Actions.View);

        return app;
    }
}
