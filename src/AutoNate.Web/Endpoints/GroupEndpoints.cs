using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Authorization;

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
            CancellationToken ct) =>
                Results.Ok(await store.ListAuthorizedAsync(http.User, includeArchived ?? false, ct)));

        group.MapGet("/{id:guid}", async (Guid id, IGroupStore store, CancellationToken ct) =>
        {
            var grp = await store.GetAsync(id, ct);
            return grp is null ? Results.NotFound() : Results.Ok(grp);
        }).RequirePermission(EntityKinds.Group, Actions.View);

        group.MapPost("/", async (
            CreateGroupRequest request,
            HttpContext http,
            IGroupStore store,
            CancellationToken ct) =>
        {
            try
            {
                var grp = await store.CreateAsync(
                    new CreateGroupInput(request.Name, request.Description),
                    ActorId(http), ct);
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
            CancellationToken ct) =>
        {
            try
            {
                var grp = await store.UpdateAsync(id,
                    new UpdateGroupInput(request.Name, request.Description),
                    ActorId(http), ct);
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
            Guid id, HttpContext http, IGroupStore store, CancellationToken ct) =>
        {
            try
            {
                var grp = await store.SetArchivedAsync(id, archived: true, ActorId(http), ct);
                return Results.Ok(grp);
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Edit);

        group.MapPost("/{id:guid}/restore", async (
            Guid id, HttpContext http, IGroupStore store, CancellationToken ct) =>
        {
            try
            {
                var grp = await store.SetArchivedAsync(id, archived: false, ActorId(http), ct);
                return Results.Ok(grp);
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IGroupStore store, CancellationToken ct) =>
        {
            var ok = await store.DeleteAsync(id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.Delete);

        // Members
        group.MapGet("/{id:guid}/members",
            async (Guid id, IGroupStore store, CancellationToken ct) =>
                Results.Ok(await store.ListMembersAsync(id, ct)))
            .RequirePermission(EntityKinds.Group, Actions.View);

        group.MapPost("/{id:guid}/members", async (
            Guid id,
            AddMemberRequest request,
            HttpContext http,
            IGroupStore store,
            CancellationToken ct) =>
        {
            try
            {
                var added = await store.AddMemberAsync(id, request.UserId, ActorId(http), ct);
                return added ? Results.NoContent() : Results.Conflict(new { error = "already a member" });
            }
            catch (GroupNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Group, Actions.AddMember);

        group.MapDelete("/{id:guid}/members/{userId:guid}",
            async (Guid id, Guid userId, IGroupStore store, CancellationToken ct) =>
            {
                var removed = await store.RemoveMemberAsync(id, userId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
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
