using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using Microsoft.Extensions.Options;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/roles").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http, IRoleStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var roles = await store.ListAuthorizedAsync(http.User, ct);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.RoleListViewed,
                IamResourceKinds.Role,
                resource: null,
                details: new { resultCount = roles.Count },
                ct);
            return Results.Ok(roles);
        }).AuthorizedInHandler("store.ListAuthorizedAsync filters via FilterQueryAsync(Role, View) against the actor's grants");

        group.MapGet("/{id:guid}", async (
            Guid id, IRoleStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var role = await store.GetAsync(id, ct);
            if (role is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.RoleViewed,
                IamResourceKinds.Role,
                resource: new { id = role.Id, name = role.Name },
                details: null,
                ct);
            return Results.Ok(role);
        }).RequirePermission(EntityKinds.Role, Actions.View);

        group.MapPost("/", async (
            CreateRoleRequest request,
            HttpContext http,
            IRoleStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var role = await store.CreateAsync(
                    new CreateRoleInput(request.Name, request.Description),
                    http.GetActorId(), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.RoleCreated,
                    IamResourceKinds.Role,
                    resource: new { id = role.Id, name = role.Name },
                    details: null,
                    ct);
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
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var role = await store.UpdateAsync(
                    id, new UpdateRoleInput(request.Name, request.Description),
                    http.GetActorId(), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.RoleUpdated,
                    IamResourceKinds.Role,
                    resource: new { id = role.Id, name = role.Name },
                    details: null,
                    ct);
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

        group.MapDelete("/{id:guid}", async (
            Guid id, IRoleStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                // Snapshot the role before delete so the audit log shows
                // the role name instead of a UUID.
                var snapshot = await store.GetAsync(id, ct);
                var deleted = await store.DeleteAsync(id, ct);
                if (!deleted) return Results.NotFound();
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.RoleDeleted,
                    IamResourceKinds.Role,
                    resource: new { id, name = snapshot?.Name },
                    details: null,
                    ct);
                return Results.NoContent();
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
            Guid id, IRoleAssignmentStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
            {
                var assignments = await store.ListByRoleAsync(id, ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.RoleAssignmentsViewed,
                    IamResourceKinds.RoleAssignment,
                    resource: new { roleId = id },
                    details: new { resultCount = assignments.Count },
                    ct);
                return Results.Ok(assignments);
            })
            .RequirePermission(EntityKinds.Role, Actions.View);

        group.MapPost("/{id:guid}/assignments", async (
            Guid id,
            CreateAssignmentRequest request,
            HttpContext http,
            IRoleAssignmentStore store,
            IAuthorizer authorizer,
            IOptions<AutoNate.Web.Authorization.AuthorizationOptions> authOptions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // role:assign used to be transitively equivalent to super-admin.
            // Nothing here restricted *which* role could be handed out or to
            // whom, and Authorizer re-reads role assignments per request — so
            // a holder of `assign` on `/role/*` could grant themselves
            // SuperAdmin and be one on the very next request.
            //
            // Two guards, both scoped to closing that path rather than
            // redesigning delegation:
            //
            //   1. Only a super-admin may hand out SuperAdmin. Otherwise the
            //      most privileged role in the system is delegable by someone
            //      who was only trusted to manage role membership.
            //   2. Nobody may assign a role to themselves unless they are a
            //      super-admin. Self-assignment is how a limited grant turns
            //      into an unlimited one; assigning to *others* stays open, so
            //      ordinary delegation is unaffected.
            //
            // Two colluding assigners can still escalate each other. That is
            // the standard separation-of-duties trade-off and is a deliberate
            // stopping point: the general rule ("you may only delegate
            // permissions you already hold") needs a role-subset comparison
            // that does not exist here yet.
            // Only under full enforcement. Every other decision point
            // short-circuits to allow when authorization is disabled or still
            // in read-only rollout, and a guard that denies where the rest of
            // the system allows would make this endpoint the odd one out —
            // and would break the staged "filter reads first, then enforce
            // writes" rollout the options exist to support. There is no
            // escalation to prevent while nothing is being enforced.
            var options = authOptions.Value;
            var enforcing = options.Enabled
                && options.Enforcement == AuthorizationEnforcement.Full;
            var capabilities = enforcing
                ? await authorizer.GetCapabilitiesAsync(http.User, ct)
                : null;
            if (enforcing && capabilities is { IsSuperAdmin: false })
            {
                if (id == SystemRoles.SuperAdminId)
                {
                    return Results.Json(
                        new { error = "Only a super-admin can assign the SuperAdmin role." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var actorId = http.GetActorId();
                if (string.Equals(request.PrincipalKind, EntityKinds.User, StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(request.PrincipalId, out var principalId)
                    && principalId == actorId)
                {
                    return Results.Json(
                        new { error = "You cannot assign a role to yourself." },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }

            try
            {
                var assignment = await store.AssignAsync(
                    new CreateRoleAssignmentInput(
                        id, request.PrincipalKind, request.PrincipalId, request.ScopeString),
                    http.GetActorId(), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.RoleAssignmentGranted,
                    IamResourceKinds.RoleAssignment,
                    resource: new
                    {
                        id = assignment.Id,
                        roleId = id,
                        principalKind = request.PrincipalKind,
                        principalId = request.PrincipalId
                    },
                    details: new { scopeString = request.ScopeString },
                    ct);
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
    public sealed record CreateRoleRequest(string Name, string? Description);
    public sealed record UpdateRoleRequest(string? Name, string? Description);
    public sealed record CreateAssignmentRequest(
        string PrincipalKind, string PrincipalId, string? ScopeString);
}
