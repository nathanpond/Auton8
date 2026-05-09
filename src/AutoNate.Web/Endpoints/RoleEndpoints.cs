using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
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
        });

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
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
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
