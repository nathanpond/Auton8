using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Menus;

namespace AutoNate.Web.Endpoints;

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        // Public read group: any authenticated user, item-level permission_required
        // is enforced inside the store while building the tree.
        var publicGroup = app.MapGroup("/api/menus").RequireAuthorization();

        publicGroup.MapGet("/{key}", async (
            string key,
            HttpContext http,
            IMenuStore store,
            CancellationToken ct) =>
        {
            var menu = await store.GetMenuTreeForActorAsync(key, http.User, ct);
            return menu is null ? Results.NotFound() : Results.Ok(menu);
        });

        // Admin write group: only users with site-config edit permissions.
        var adminGroup = app.MapGroup("/api/admin/menus").RequireAuthorization();

        adminGroup.MapGet("/", async (IMenuStore store, CancellationToken ct) =>
            Results.Ok(await store.ListMenusAsync(ct)))
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        adminGroup.MapGet("/{key}", async (string key, IMenuStore store, CancellationToken ct) =>
        {
            var menu = await store.GetMenuTreeAsync(key, ct);
            return menu is null ? Results.NotFound() : Results.Ok(menu);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        adminGroup.MapPost("/", async (
            CreateMenuRequest request,
            HttpContext http,
            IMenuStore store,
            CancellationToken ct) =>
        {
            try
            {
                var menu = await store.CreateMenuAsync(
                    new CreateMenuInput(request.Key, request.Name, request.Description),
                    ActorId(http), ct);
                return Results.Created($"/api/admin/menus/{menu.Key}", menu);
            }
            catch (MenuValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateMenuRequest request,
            HttpContext http,
            IMenuStore store,
            CancellationToken ct) =>
        {
            try
            {
                var menu = await store.UpdateMenuAsync(
                    id, new UpdateMenuInput(request.Name, request.Description),
                    ActorId(http), ct);
                return Results.Ok(menu);
            }
            catch (MenuNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapDelete("/{id:guid}", async (Guid id, IMenuStore store, CancellationToken ct) =>
        {
            try
            {
                var deleted = await store.DeleteMenuAsync(id, ct);
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Delete);

        adminGroup.MapPost("/{key}/items", async (
            string key,
            CreateMenuItemRequest request,
            IMenuStore store,
            CancellationToken ct) =>
        {
            try
            {
                var item = await store.CreateItemAsync(key, new CreateMenuItemInput(
                    request.ParentId,
                    request.SortOrder ?? 0,
                    request.DisplayName,
                    request.Icon,
                    request.ItemType,
                    request.Config,
                    request.PermissionRequired,
                    request.IsVisible ?? true), ct);
                return Results.Created($"/api/admin/menus/items/{item.Id}", item);
            }
            catch (MenuNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapPatch("/items/{id:guid}", async (
            Guid id,
            UpdateMenuItemRequest request,
            IMenuStore store,
            CancellationToken ct) =>
        {
            try
            {
                var input = new UpdateMenuItemInput(
                    request.ParentId,
                    request.SortOrder,
                    request.DisplayName,
                    request.Icon,
                    request.ItemType,
                    request.Config,
                    request.PermissionRequired,
                    request.IsVisible)
                {
                    ClearIcon = request.ClearIcon,
                    ClearPermissionRequired = request.ClearPermissionRequired
                };
                var item = await store.UpdateItemAsync(id, input, ct);
                return Results.Ok(item);
            }
            catch (MenuItemNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapDelete("/items/{id:guid}", async (Guid id, IMenuStore store, CancellationToken ct) =>
        {
            try
            {
                var deleted = await store.DeleteItemAsync(id, ct);
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapPut("/{key}/tree", async (
            string key,
            ReplaceTreeRequest request,
            IMenuStore store,
            CancellationToken ct) =>
        {
            try
            {
                var nodes = (request.Nodes ?? Array.Empty<TreeNodeRequest>())
                    .Select(n => new TreeNodeInput(n.Id, n.ParentId, n.SortOrder))
                    .ToList();
                await store.ReplaceTreeAsync(key, nodes, ct);
                return Results.NoContent();
            }
            catch (MenuNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public sealed record CreateMenuRequest(string Key, string Name, string? Description);
    public sealed record UpdateMenuRequest(string? Name, string? Description);
    public sealed record CreateMenuItemRequest(
        Guid? ParentId,
        int? SortOrder,
        string DisplayName,
        string? Icon,
        string ItemType,
        JsonElement Config,
        string? PermissionRequired,
        bool? IsVisible);
    public sealed record UpdateMenuItemRequest(
        Guid? ParentId,
        int? SortOrder,
        string? DisplayName,
        string? Icon,
        string? ItemType,
        JsonElement? Config,
        string? PermissionRequired,
        bool? IsVisible,
        bool ClearIcon = false,
        bool ClearPermissionRequired = false);
    public sealed record ReplaceTreeRequest(IReadOnlyList<TreeNodeRequest> Nodes);
    public sealed record TreeNodeRequest(Guid Id, Guid? ParentId, int SortOrder);
}
