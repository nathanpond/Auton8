using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models.Menus;

namespace AutoNate.Web.Services.Menus;

public sealed record class CreateMenuInput(string Key, string Name, string? Description);

public sealed record class UpdateMenuInput(string? Name, string? Description);

public sealed record class CreateMenuItemInput(
    Guid? ParentId,
    int SortOrder,
    string DisplayName,
    string? Icon,
    string ItemType,
    JsonElement Config,
    string? PermissionRequired,
    bool IsVisible);

public sealed record class UpdateMenuItemInput(
    Guid? ParentId,
    int? SortOrder,
    string? DisplayName,
    string? Icon,
    string? ItemType,
    JsonElement? Config,
    string? PermissionRequired,
    bool? IsVisible)
{
    public bool ClearPermissionRequired { get; init; }
    public bool ClearIcon { get; init; }
}

public sealed record class TreeNodeInput(
    Guid Id,
    Guid? ParentId,
    int SortOrder);

public sealed class MenuNotFoundException(string identifier)
    : Exception($"Menu '{identifier}' was not found.");

public sealed class MenuItemNotFoundException(Guid id)
    : Exception($"Menu item '{id}' was not found.");

public sealed class MenuValidationException(string message) : Exception(message);

public sealed record class PageRegistryEntry(Guid Id, string Path, string ContentType);

// Config carries mount-point-specific JSON from menu_items.config minus the
// reserved templateKey / path / aliasPath fields. Templates that need
// per-mount configuration (e.g. the dashboard template's
// `isUserConfigurable` + `defaultLayout`) read it client-side via
// useTemplateConfig(). Null when the mount has no extra config.
public sealed record class PageContent(Guid Id, string Path, string Content, string ContentType, JsonElement? Config);

public interface IMenuStore
{
    Task<IReadOnlyList<Menu>> ListMenusAsync(CancellationToken cancellationToken = default);

    Task<Menu?> GetMenuTreeAsync(string key, CancellationToken cancellationToken = default);

    Task<Menu?> GetMenuTreeForActorAsync(string key, ClaimsPrincipal actor, CancellationToken cancellationToken = default);

    Task<Menu> CreateMenuAsync(CreateMenuInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<Menu> UpdateMenuAsync(Guid id, UpdateMenuInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> DeleteMenuAsync(Guid id, CancellationToken cancellationToken = default);

    // Cheap by-id lookups callers use to snapshot a row's name into audit
    // events before deletion, so the audit log shows "Site Menu" instead of
    // a bare UUID. Both return null when the id doesn't resolve.
    Task<Menu?> GetMenuByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MenuItem?> GetItemByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MenuItem> CreateItemAsync(string menuKey, CreateMenuItemInput input, CancellationToken cancellationToken = default);

    Task<MenuItem> UpdateItemAsync(Guid id, UpdateMenuItemInput input, CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task ReplaceTreeAsync(string menuKey, IReadOnlyList<TreeNodeInput> nodes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageRegistryEntry>> ListPagesAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default);

    Task<PageContent?> GetPageByPathAsync(string path, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}
