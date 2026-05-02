namespace AutoNate.Web.Services.SiteSettings;

// Topic + event-type names for the site.events bus topic. Phase 3 of the
// audit-events plan introduces this domain — every change to the navigation
// menus, site settings, branding/appearance, and status-color palette
// publishes an event so an audit consumer can answer "who changed the
// site configuration and when?"
public static class SiteEventTopic
{
    public const string TopicRoot = "site";
    public const string TopicName = "site.events";
}

public static class SiteResourceKinds
{
    public const string Menu = "menu";
    public const string MenuItem = "menu.item";
    public const string MenuTree = "menu.tree";
    public const string Settings = "settings";
    public const string Appearance = "appearance";
    public const string StatusAppearance = "status.appearance";
    public const string Page = "page";
    public const string PageTemplate = "page.template";
    public const string EventCatalog = "event.catalog";
}

public static class SiteEventTypes
{
    // Menu lifecycle
    public const string MenuCreated = "site.menu.created";
    public const string MenuUpdated = "site.menu.updated";
    public const string MenuDeleted = "site.menu.deleted";
    public const string MenuItemCreated = "site.menu.item.created";
    public const string MenuItemUpdated = "site.menu.item.updated";
    public const string MenuItemDeleted = "site.menu.item.deleted";
    public const string MenuTreeReplaced = "site.menu.tree.replaced";

    // Site settings
    public const string SettingsUpdated = "site.settings.updated";

    // Appearance
    public const string AppearanceUpdated = "site.appearance.updated";

    // Status appearance (per-status color palette)
    public const string StatusAppearanceCreated = "site.status-appearance.created";
    public const string StatusAppearanceUpdated = "site.status-appearance.updated";
    public const string StatusAppearanceDeleted = "site.status-appearance.deleted";

    // View events (Phase 4)
    public const string MenuListViewed = "site.menu.list.viewed";
    public const string MenuViewed = "site.menu.viewed";
    public const string SettingsListViewed = "site.settings.list.viewed";
    public const string AppearanceViewed = "site.appearance.viewed";
    public const string StatusAppearanceListViewed = "site.status-appearance.list.viewed";

    // Pages registry / templates / event catalog reads. Surfaced so the
    // audit log captures who walked the page registry, who browsed the
    // page-template picker, and who opened the event catalog modal.
    public const string PageListViewed = "site.page.list.viewed";
    public const string PageLookupViewed = "site.page.lookup.viewed";
    public const string PageTemplateListViewed = "site.page-template.list.viewed";
    public const string EventCatalogViewed = "site.event-catalog.viewed";
}
