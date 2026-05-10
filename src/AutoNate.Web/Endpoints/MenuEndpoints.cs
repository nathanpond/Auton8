using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Menus;
using AutoNate.Web.Services.SiteSettings;

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
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var menu = await store.GetMenuTreeForActorAsync(key, http.User, ct);
            if (menu is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.MenuViewed,
                SiteResourceKinds.Menu,
                resource: new { key },
                details: new { scope = "actor" },
                ct);
            return Results.Ok(menu);
        }).AuthorizedInHandler("store.GetMenuTreeForActorAsync filters items by per-item permission_required against the actor's grants");

        // Admin write group: only users with site-config edit permissions.
        var adminGroup = app.MapGroup("/api/admin/menus").RequireAuthorization();

        adminGroup.MapGet("/", async (
            IMenuStore store, IAuditEventPublisher auditPublisher, CancellationToken ct) =>
            {
                var menus = await store.ListMenusAsync(ct);
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuListViewed,
                    SiteResourceKinds.Menu,
                    resource: null,
                    details: new { resultCount = menus.Count },
                    ct);
                return Results.Ok(menus);
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        adminGroup.MapGet("/{key}", async (
            string key, IMenuStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var menu = await store.GetMenuTreeAsync(key, ct);
            if (menu is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.MenuViewed,
                SiteResourceKinds.Menu,
                resource: new { key },
                details: new { scope = "admin" },
                ct);
            return Results.Ok(menu);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        adminGroup.MapPost("/", async (
            CreateMenuRequest request,
            HttpContext http,
            IMenuStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var menu = await store.CreateMenuAsync(
                    new CreateMenuInput(request.Key, request.Name, request.Description),
                    http.GetActorId(), ct);
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuCreated,
                    SiteResourceKinds.Menu,
                    resource: new { id = menu.Id, key = menu.Key, name = menu.Name },
                    details: null,
                    ct);
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
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var menu = await store.UpdateMenuAsync(
                    id, new UpdateMenuInput(request.Name, request.Description),
                    http.GetActorId(), ct);
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuUpdated,
                    SiteResourceKinds.Menu,
                    resource: new { id = menu.Id, key = menu.Key, name = menu.Name },
                    details: null,
                    ct);
                return Results.Ok(menu);
            }
            catch (MenuNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapDelete("/{id:guid}", async (
            Guid id, IMenuStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                // Snapshot the menu before delete so the audit log shows
                // "Site Menu" instead of a bare UUID.
                var snapshot = await store.GetMenuByIdAsync(id, ct);
                var deleted = await store.DeleteMenuAsync(id, ct);
                if (!deleted) return Results.NotFound();
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuDeleted,
                    SiteResourceKinds.Menu,
                    resource: new { id, key = snapshot?.Key, name = snapshot?.Name },
                    details: null,
                    ct);
                return Results.NoContent();
            }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Delete);

        adminGroup.MapPost("/{key}/items", async (
            string key,
            CreateMenuItemRequest request,
            IMenuStore store,
            IAuditEventPublisher auditPublisher,
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
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuItemCreated,
                    SiteResourceKinds.MenuItem,
                    resource: new { id = item.Id, menuKey = key, displayName = item.DisplayName },
                    details: null,
                    ct);
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
            IAuditEventPublisher auditPublisher,
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
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuItemUpdated,
                    SiteResourceKinds.MenuItem,
                    resource: new { id = item.Id, displayName = item.DisplayName },
                    details: null,
                    ct);
                return Results.Ok(item);
            }
            catch (MenuItemNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapDelete("/items/{id:guid}", async (
            Guid id, IMenuStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                var snapshot = await store.GetItemByIdAsync(id, ct);
                var deleted = await store.DeleteItemAsync(id, ct);
                if (!deleted) return Results.NotFound();
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuItemDeleted,
                    SiteResourceKinds.MenuItem,
                    resource: new { id, displayName = snapshot?.DisplayName },
                    details: null,
                    ct);
                return Results.NoContent();
            }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        adminGroup.MapPut("/{key}/tree", async (
            string key,
            ReplaceTreeRequest request,
            IMenuStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var nodes = (request.Nodes ?? Array.Empty<TreeNodeRequest>())
                    .Select(n => new TreeNodeInput(n.Id, n.ParentId, n.SortOrder))
                    .ToList();
                await store.ReplaceTreeAsync(key, nodes, ct);
                await auditPublisher.PublishAsync(
                    SiteEventTopic.TopicName,
                    SiteEventTypes.MenuTreeReplaced,
                    SiteResourceKinds.MenuTree,
                    resource: new { menuKey = key },
                    details: new { nodeCount = nodes.Count },
                    ct);
                return Results.NoContent();
            }
            catch (MenuNotFoundException) { return Results.NotFound(); }
            catch (MenuValidationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
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
