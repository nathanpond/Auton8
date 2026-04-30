using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/groups").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http,
            bool? includeArchived,
            IGroupStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var groups = await store.ListAuthorizedAsync(http.User, includeArchived ?? false, ct);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.GroupListViewed,
                IamResourceKinds.Group,
                resource: null,
                details: new { resultCount = groups.Count, includeArchived = includeArchived ?? false },
                ct);
            return Results.Ok(groups);
        });

        group.MapGet("/{id:guid}", async (
            Guid id, IGroupStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var grp = await store.GetAsync(id, ct);
            if (grp is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.GroupViewed,
                IamResourceKinds.Group,
                resource: new { id = grp.Id, name = grp.Name },
                details: null,
                ct);
            return Results.Ok(grp);
        }).RequirePermission(EntityKinds.Group, Actions.View);

        group.MapPost("/", async (
            CreateGroupRequest request,
            HttpContext http,
            IGroupStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var grp = await store.CreateAsync(
                    new CreateGroupInput(request.Name, request.Description),
                    ActorId(http), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupCreated,
                    IamResourceKinds.Group,
                    resource: new { id = grp.Id, name = grp.Name },
                    details: null,
                    ct);
                return Results.Created($"/api/admin/groups/{grp.Id}", grp);
            }
            catch (GroupValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.Group, Actions.Create);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateGroupRequest request,
            HttpContext http,
            IGroupStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var grp = await store.UpdateAsync(id,
                    new UpdateGroupInput(request.Name, request.Description),
                    ActorId(http), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupUpdated,
                    IamResourceKinds.Group,
                    resource: new { id = grp.Id, name = grp.Name },
                    details: null,
                    ct);
                return Results.Ok(grp);
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
            catch (GroupValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Edit);

        group.MapPost("/{id:guid}/archive", async (
            Guid id, HttpContext http, IGroupStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                var grp = await store.SetArchivedAsync(id, archived: true, ActorId(http), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupArchived,
                    IamResourceKinds.Group,
                    resource: new { id = grp.Id, name = grp.Name },
                    details: null,
                    ct);
                return Results.Ok(grp);
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Edit);

        group.MapPost("/{id:guid}/restore", async (
            Guid id, HttpContext http, IGroupStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                var grp = await store.SetArchivedAsync(id, archived: false, ActorId(http), ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupRestored,
                    IamResourceKinds.Group,
                    resource: new { id = grp.Id, name = grp.Name },
                    details: null,
                    ct);
                return Results.Ok(grp);
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, IGroupStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var ok = await store.DeleteAsync(id, ct);
            if (!ok) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.GroupDeleted,
                IamResourceKinds.Group,
                resource: new { id },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Delete);

        // Members
        group.MapGet("/{id:guid}/members",
            async (Guid id, IGroupStore store,
                IAuditEventPublisher auditPublisher, CancellationToken ct) =>
            {
                var members = await store.ListMembersAsync(id, ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupMembersViewed,
                    IamResourceKinds.GroupMember,
                    resource: new { groupId = id },
                    details: new { resultCount = members.Count },
                    ct);
                return Results.Ok(members);
            })
            .RequirePermission(EntityKinds.Group, Actions.View);

        group.MapPost("/{id:guid}/members", async (
            Guid id,
            AddMemberRequest request,
            HttpContext http,
            IGroupStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var added = await store.AddMemberAsync(id, request.UserId, ActorId(http), ct);
                if (!added) return Results.Conflict(new { error = "already a member" });
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupMemberAdded,
                    IamResourceKinds.GroupMember,
                    resource: new { groupId = id, userId = request.UserId },
                    details: null,
                    ct);
                return Results.NoContent();
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.AddMember);

        group.MapDelete("/{id:guid}/members/{userId:guid}",
            async (Guid id, Guid userId, IGroupStore store,
                IAuditEventPublisher auditPublisher, CancellationToken ct) =>
            {
                var removed = await store.RemoveMemberAsync(id, userId, ct);
                if (!removed) return Results.NotFound();
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.GroupMemberRemoved,
                    IamResourceKinds.GroupMember,
                    resource: new { groupId = id, userId },
                    details: null,
                    ct);
                return Results.NoContent();
            }).DisableAntiforgery()
              .RequirePermission(EntityKinds.Group, Actions.RemoveMember);

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public sealed record CreateGroupRequest(string Name, string? Description);
    public sealed record UpdateGroupRequest(string? Name, string? Description);
    public sealed record AddMemberRequest(Guid UserId);
}
