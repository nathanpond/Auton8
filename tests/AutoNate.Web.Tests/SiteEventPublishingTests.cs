using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.SiteSettings;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class SiteEventPublishingTests
{
    private sealed record MenuDto(Guid Id, string Key, string Name);
    private sealed record MenuItemDto(Guid Id, string DisplayName);
    private sealed record StatusAppearanceDto(Guid Id, string Status, string Color);

    [Fact]
    public async Task Menu_lifecycle_publishes_created_updated_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        var key = "test-" + Guid.NewGuid().ToString("N")[..8];
        var create = await client.PostAsJsonAsync(
            "/api/admin/menus/",
            new MenuEndpoints.CreateMenuRequest(key, "Test Menu", null));
        create.EnsureSuccessStatusCode();
        var menu = (await create.Content.ReadFromJsonAsync<MenuDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/admin/menus/{menu.Id}",
            new MenuEndpoints.UpdateMenuRequest("Test Menu v2", null))).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/admin/menus/{menu.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(SiteEventTypes.MenuCreated, types);
        Assert.Contains(SiteEventTypes.MenuUpdated, types);
        Assert.Contains(SiteEventTypes.MenuDeleted, types);
    }

    [Fact]
    public async Task MenuItem_lifecycle_publishes_each_event()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);

        var key = "im-" + Guid.NewGuid().ToString("N")[..8];
        var create = await client.PostAsJsonAsync(
            "/api/admin/menus/",
            new MenuEndpoints.CreateMenuRequest(key, "Item Menu", null));
        create.EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        var itemCreate = await client.PostAsJsonAsync(
            $"/api/admin/menus/{key}/items",
            new MenuEndpoints.CreateMenuItemRequest(
                ParentId: null,
                SortOrder: 0,
                DisplayName: "Home",
                Icon: null,
                ItemType: "link",
                Config: JsonDocument.Parse("{\"path\":\"/home\"}").RootElement.Clone(),
                PermissionRequired: null,
                IsVisible: true));
        itemCreate.EnsureSuccessStatusCode();
        var item = (await itemCreate.Content.ReadFromJsonAsync<MenuItemDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/admin/menus/items/{item.Id}",
            new MenuEndpoints.UpdateMenuItemRequest(
                ParentId: null,
                SortOrder: 1,
                DisplayName: "Home v2",
                Icon: null,
                ItemType: null,
                Config: null,
                PermissionRequired: null,
                IsVisible: null))).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(
            $"/api/admin/menus/{key}/tree",
            new MenuEndpoints.ReplaceTreeRequest(new[]
            {
                new MenuEndpoints.TreeNodeRequest(item.Id, null, 0)
            }))).EnsureSuccessStatusCode();

        (await client.DeleteAsync($"/api/admin/menus/items/{item.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(SiteEventTypes.MenuItemCreated, types);
        Assert.Contains(SiteEventTypes.MenuItemUpdated, types);
        Assert.Contains(SiteEventTypes.MenuTreeReplaced, types);
        Assert.Contains(SiteEventTypes.MenuItemDeleted, types);
    }

    [Fact]
    public async Task Settings_update_publishes_settings_updated()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        // Pick the first available setting key from the registry.
        var firstSetting = SiteSettingsRegistry.All.First();
        var updates = new Dictionary<string, JsonElement>
        {
            [firstSetting.Key] = firstSetting.DefaultValue
        };

        var response = await client.PatchAsJsonAsync(
            "/api/admin/site-settings/",
            new UpdateSiteSettingsRequest(updates));
        response.EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == SiteEventTypes.SettingsUpdated);
    }

    [Fact]
    public async Task Appearance_update_publishes_appearance_updated()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        var current = await client.GetFromJsonAsync<SiteAppearanceDto>("/api/admin/appearance/");
        Assert.NotNull(current);
        factory.RecordedAuditEvents.Clear();

        // Round-trip the existing record so all required fields are set; we only
        // need to assert the publish, not change the look.
        var request = new UpdateSiteAppearanceRequest(
            current!.SiteName, current.LogoMode, current.LogoImageUrl, current.LogoIcon,
            current.LogoText, current.LoginTagline, current.LoginCoverImageUrl,
            current.PrimaryAccentColor, current.HeaderBg, current.HeaderColor,
            current.TopMenuBg, current.TopMenuLinkColor, current.TopMenuLinkHoverBg,
            current.TopMenuLinkHoverColor, current.TopMenuLinkActiveBg, current.TopMenuLinkActiveColor,
            current.SidebarBg, current.SidebarLinkColor, current.SidebarLinkHoverColor,
            current.SidebarActiveBg, current.SidebarActiveColor, current.SidebarIconColor,
            current.SidebarSubmenuBg, current.SidebarSectionColor,
            current.SurfaceBg, current.SurfaceSecondaryBg, current.SurfaceTextColor,
            current.BorderColor, current.DropdownBg, current.ModalBg,
            current.SecondaryButtonBg, current.SecondaryButtonTextColor,
            current.SecondaryButtonBorderColor, current.SecondaryButtonHoverBg,
            current.SecondaryButtonHoverTextColor);

        (await client.PatchAsJsonAsync("/api/admin/appearance/", request)).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == SiteEventTypes.AppearanceUpdated);
    }

    [Fact]
    public async Task StatusAppearance_lifecycle_publishes_created_updated_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        var unique = "Test-" + Guid.NewGuid().ToString("N")[..8];
        var create = await client.PostAsJsonAsync(
            "/api/admin/status-appearance/",
            new StatusAppearanceEndpoints.CreateStatusAppearanceRequest(unique, "#abcdef"));
        create.EnsureSuccessStatusCode();
        var entry = (await create.Content.ReadFromJsonAsync<StatusAppearanceDto>())!;

        (await client.PatchAsJsonAsync(
            $"/api/admin/status-appearance/{entry.Id}",
            new StatusAppearanceEndpoints.UpdateStatusAppearanceRequest(unique + "X", "#fedcba")))
            .EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/admin/status-appearance/{entry.Id}")).EnsureSuccessStatusCode();

        var types = factory.RecordedAuditEvents.Events.Select(e => e.EventType).ToArray();
        Assert.Contains(SiteEventTypes.StatusAppearanceCreated, types);
        Assert.Contains(SiteEventTypes.StatusAppearanceUpdated, types);
        Assert.Contains(SiteEventTypes.StatusAppearanceDeleted, types);
    }

    private static async Task Prime(HttpClient client) =>
        (await client.GetAsync("/api/admin/menus/")).EnsureSuccessStatusCode();
}
