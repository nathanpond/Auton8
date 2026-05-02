namespace AutoNate.Plugins.Abstractions;

// Plugin-facing helpers for adding menu items into the host's menu system.
// Items inserted through this surface are tagged with the plugin's ID and
// auto-removed when the plugin is disabled or deleted, so plugin authors can
// declaratively re-register their menus on every enable inside Configure().
//
// All helpers are synchronous to match Configure(IPluginContext)'s sync
// signature; internally they use Npgsql's sync connection methods.
public interface IPluginMenus
{
    // Snapshot of every menu and its items so a plugin can introspect what
    // already exists (e.g. to find the parent_id of an existing group it wants
    // to nest under). Returns the same data the admin Pages / Menus page sees.
    IReadOnlyList<MenuInfo> ListMenus();

    // Adds a single item under the "Plugins" group in the Site Configuration
    // sidebar. Equivalent to AddMenuItem("site-config", <plugins-group-id>, ...)
    // but resolves the group id for you.
    Guid AddPluginMenuItem(NewMenuItem item);

    // Creates a new top-level group inside the Site Configuration menu and
    // populates it with the given children, in declared order. Returns the
    // group's id. Use this for a plugin that owns more than one config page
    // and wants its own collapsible section in the sidebar.
    Guid AddSiteConfigGroup(string displayName, string? icon, IEnumerable<NewMenuItem> children);

    // Generic insert: any item, in any menu (looked up by key), under any
    // parent (null = top-level). The plugin owns the resulting row's lifecycle:
    // disable removes it, delete removes it, and the plugin re-adds it on the
    // next enable inside Configure().
    Guid AddMenuItem(string menuKey, Guid? parentId, NewMenuItem item);

    // Removes every menu_items row this plugin previously added (matched by
    // created_by_plugin_id). Mirrors the sweep the host does on disable and
    // the FK CASCADE on delete; expose it so plugins can call it explicitly
    // from Cleanup() when they want their menus gone before the host's own
    // teardown runs. Returns the number of rows removed.
    int RemoveAll();

    // Removes a single menu_items row by id IF it was added by this plugin.
    // Items the plugin doesn't own are left alone (returns false), so a
    // plugin can't sweep host or other-plugin menu items with this call —
    // useful for surgical cleanup like "remove the trailing separator I
    // added under the Settings group". Returns true when the row was
    // actually deleted.
    bool RemoveMenuItem(Guid id);
}

// Description of a menu item the plugin wants to insert. Mirrors the columns
// the admin UI lets you set, minus identity and provenance fields the host
// fills in (id, created_at, created_by, created_by_plugin_id).
//
// `Config` is JSON-serialized into the menu_items.config JSONB column. Shape
// depends on `ItemType`:
//   * "template"  → { templateKey: "...", path?: "..." }
//   * "page"      → { path: "...", content: "...", contentType: "html"|"jsx" }
//   * "link"      → { href: "https://..." }
//   * "action"    → { action: "..." }
//   * "separator" → {}
//   * "group"     → { startsExpanded?: bool, dynamicChildren?: "..." }
public sealed record NewMenuItem(
    string DisplayName,
    string ItemType,
    string? Icon = null,
    object? Config = null,
    int? SortOrder = null,
    bool IsVisible = true);

public sealed record MenuInfo(
    Guid Id,
    string Key,
    string Name,
    IReadOnlyList<MenuItemInfo> Items);

public sealed record MenuItemInfo(
    Guid Id,
    Guid? ParentId,
    int SortOrder,
    string DisplayName,
    string? Icon,
    string ItemType,
    string ConfigJson,
    bool IsVisible,
    bool IsSystem,
    Guid? CreatedByPluginId);
