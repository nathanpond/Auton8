using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class ProjectMemberEndpoints
{
    public static IEndpointRouteBuilder MapProjectMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/projects/{projectId:guid}/members")
            .RequireAuthorization();

        // Listing memberships requires View on the project — anyone who can
        // see the project can see who else has access to it.
        group.MapGet("/", async (
            Guid projectId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await memberships.ListMembersAsync(db, projectId, ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectMemberListViewed,
                ContentResourceKinds.ProjectMember,
                resource: new { projectId },
                details: new { resultCount = rows.Count },
                ct);
            return Results.Ok(rows.Select(m => new ProjectMemberDto(
                m.ProjectId, m.UserId, m.Role,
                m.AddedAtUtc, m.AddedBy, m.UpdatedAtUtc, m.UpdatedBy)));
        }).RequirePermission(EntityKinds.Project, Actions.View, "projectId");

        // Owner-only: upsert another user's role on the project.
        group.MapPut("/{userId:guid}", async (
            Guid projectId,
            Guid userId,
            SetMemberRoleRequest request,
            HttpContext http,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!await authorizer.IsProjectOwnerAsync(http.User, projectId, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var role = ProjectRoleNames.TryParse(request.Role);
            if (role is null)
            {
                return Results.BadRequest(new { error = "Role must be owner | contributor | viewer." });
            }
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var prior = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .FirstOrDefaultAsync(ct);

            try
            {
                await memberships.SetRoleAsync(db, projectId, userId, role.Value,
                    actorId, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            var eventType = prior is null
                ? ContentEventTypes.ProjectMemberAdded
                : ContentEventTypes.ProjectMemberRoleChanged;
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                eventType,
                ContentResourceKinds.ProjectMember,
                resource: new { projectId, userId, role = ProjectRoleNames.ToWire(role.Value) },
                details: prior is null ? null : new { previousRole = prior },
                ct);

            return Results.Ok(new { projectId, userId, role = ProjectRoleNames.ToWire(role.Value) });
        }).DisableAntiforgery();

        // Owner-only: remove a member. Refused if it would remove the last
        // owner.
        group.MapDelete("/{userId:guid}", async (
            Guid projectId,
            Guid userId,
            HttpContext http,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!await authorizer.IsProjectOwnerAsync(http.User, projectId, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var prior = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .FirstOrDefaultAsync(ct);
            if (prior is null) return Results.NotFound();

            try
            {
                await memberships.RemoveMemberAsync(db, projectId, userId, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectMemberRemoved,
                ContentResourceKinds.ProjectMember,
                resource: new { projectId, userId },
                details: new { previousRole = prior },
                ct);

            return Results.NoContent();
        }).DisableAntiforgery();

        return app;
    }

    public sealed record SetMemberRoleRequest(string Role);

    public sealed record ProjectMemberDto(
        Guid ProjectId, Guid UserId, string Role,
        DateTime AddedAtUtc, Guid AddedBy, DateTime UpdatedAtUtc, Guid UpdatedBy);
}
