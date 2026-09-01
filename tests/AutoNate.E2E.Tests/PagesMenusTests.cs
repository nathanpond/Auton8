using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class PagesMenusTests : E2ETestBase
{
    public PagesMenusTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DynamicJsxPage_EditVisibilityAndDeleteLifecycle()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("jsx-page");
        var renamed = TestNames.Prefixed("jsx-page-edited");
        var path = $"/e2e-jsx-{TestNames.ShortSlug()}";
        var originalMarker = TestNames.Prefixed("jsx-original");
        await CreateStandaloneItemAsync(page.APIRequest, name, "page", new
        {
            path,
            contentType = "jsx",
            content = $"function Page() {{ return <h2>{originalMarker}</h2>; }}"
        });

        await page.GotoAsync(path);
        // 30s, not 15: a dynamic JSX page is transformed in the browser on
        // first view, and a cold CI runner exceeded 15s doing it. The same
        // ceiling the lazy-chunk assertions elsewhere use.
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = originalMarker }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await OpenStandaloneMenuAsync(page);
        var row = MenuRow(page, name);
        await row.GetByRole(AriaRole.Button, new() { Name = "Toggle visibility" }).ClickAsync();
        await Assertions.Expect(row.GetByText("hidden", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Toggle visibility" }).ClickAsync();

        await row.GetByRole(AriaRole.Button, new() { Name = "Edit item" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit menu item" });
        await dialog.GetByLabel("Display name").FillAsync(renamed);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save and Close" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GotoAsync(path);
        // 30s, not 15: a dynamic JSX page is transformed in the browser on
        // first view, and a cold CI runner exceeded 15s doing it. The same
        // ceiling the lazy-chunk assertions elsewhere use.
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = originalMarker }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await OpenStandaloneMenuAsync(page);
        row = MenuRow(page, renamed);
        Task? acceptDialogTask = null;
        page.Dialog += (_, browserDialog) => acceptDialogTask = browserDialog.AcceptAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete item" }).ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(row).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task DynamicTemplateRoute_VisibilityAndDeleteLifecycle()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("dashboard-page");
        var path = $"/e2e-dashboard-{TestNames.ShortSlug()}";
        await CreateStandaloneItemAsync(page.APIRequest, name, "template", new
        {
            templateKey = "dashboard",
            path,
            isUserConfigurable = true
        });

        await page.GotoAsync(path);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await OpenStandaloneMenuAsync(page);
        var row = MenuRow(page, name);
        await row.GetByRole(AriaRole.Button, new() { Name = "Toggle visibility" }).ClickAsync();
        await Assertions.Expect(row.GetByText("hidden", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Toggle visibility" }).ClickAsync();

        Task? acceptDialogTask = null;
        page.Dialog += (_, browserDialog) => acceptDialogTask = browserDialog.AcceptAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete item" }).ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(row).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task MenuTree_NestingOrderingAndDeletePersist()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var key = $"e2e-menu-{TestNames.ShortSlug()}";
        var name = TestNames.Prefixed("menu-tree");
        var createMenu = await page.APIRequest.PostAsync("/api/admin/menus", new()
        {
            DataObject = new { key, name }
        });
        Assert.True(createMenu.Ok, await createMenu.TextAsync());
        var menuJson = await createMenu.JsonAsync();
        var menuId = menuJson!.Value.GetProperty("id").GetGuid();
        var rootId = await CreateMenuItemAsync(page.APIRequest, key, "Parent", "group");
        var childId = await CreateMenuItemAsync(page.APIRequest, key, "Child", "group");
        var separatorId = await CreateMenuItemAsync(page.APIRequest, key, "", "separator");
        var replace = await page.APIRequest.PutAsync($"/api/admin/menus/{key}/tree", new()
        {
            DataObject = new
            {
                nodes = new[]
                {
                    new { id = rootId, parentId = (Guid?)null, sortOrder = 0 },
                    new { id = childId, parentId = (Guid?)rootId, sortOrder = 0 },
                    new { id = separatorId, parentId = (Guid?)null, sortOrder = 1 }
                }
            }
        });
        Assert.True(replace.Ok, await replace.TextAsync());

        await page.GotoAsync("/admin/config/pages-menus");
        await page.GetByRole(AriaRole.Tab, new() { Name = name }).ClickAsync();
        var rows = page.Locator(".menu-tree-list li");
        await Assertions.Expect(rows).ToHaveCountAsync(3);
        Assert.True(await rows.Nth(1).EvaluateAsync<bool>(
            "(child, parent) => parseInt(getComputedStyle(child).paddingLeft) > parseInt(getComputedStyle(parent).paddingLeft)",
            await rows.Nth(0).ElementHandleAsync()));

        Task? acceptDialogTask = null;
        page.Dialog += (_, browserDialog) => acceptDialogTask = browserDialog.AcceptAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete menu" }).ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = name }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        _ = menuId;
    }

    [Fact]
    public async Task MenuTree_RowIsSelectableFromTheKeyboard()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var key = $"e2e-menu-{TestNames.ShortSlug()}";
        var name = TestNames.Prefixed("menu-kbd");
        var createMenu = await page.APIRequest.PostAsync("/api/admin/menus", new()
        {
            DataObject = new { key, name }
        });
        Assert.True(createMenu.Ok, await createMenu.TextAsync());
        var itemName = TestNames.Prefixed("kbd-item");
        _ = await CreateMenuItemAsync(page.APIRequest, key, itemName, "group");

        await page.GotoAsync("/admin/config/pages-menus");
        await page.GetByRole(AriaRole.Tab, new() { Name = name }).ClickAsync();

        // The row label is a real button, so it has an accessible name and
        // answers Enter. Before the fix the only click target was the bare
        // <li>, and every nested control stopPropagation'd — so a keyboard
        // user could expand, hide and delete rows but never select one.
        var label = page.GetByRole(AriaRole.Button, new() { Name = itemName });
        await Assertions.Expect(label).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(label).ToHaveAttributeAsync("aria-current", "false");

        await label.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        // Selection is now announced as well as coloured: aria-current makes
        // the state available to a screen reader, which the background-colour
        // swap alone never was.
        await Assertions.Expect(label)
            .ToHaveAttributeAsync("aria-current", "true", new() { Timeout = 5_000 });
    }

    private static async Task OpenStandaloneMenuAsync(IPage page)
    {
        await page.GotoAsync("/admin/config/pages-menus");
        await page.GetByRole(AriaRole.Tab, new() { NameRegex = new("Standalone") }).ClickAsync();
    }

    private static ILocator MenuRow(IPage page, string displayName) =>
        page.Locator(".menu-tree-list li").Filter(new() { HasText = displayName });

    private static async Task CreateStandaloneItemAsync(
        IAPIRequestContext request, string displayName, string itemType, object config)
    {
        var response = await request.PostAsync("/api/admin/menus/standalone/items", new()
        {
            DataObject = new
            {
                parentId = (Guid?)null,
                sortOrder = 0,
                displayName,
                icon = (string?)null,
                itemType,
                config = JsonSerializer.SerializeToElement(config),
                permissionRequired = (string?)null,
                isVisible = true
            }
        });
        Assert.True(response.Ok, await response.TextAsync());
    }

    private static async Task<Guid> CreateMenuItemAsync(
        IAPIRequestContext request, string menuKey, string displayName, string itemType)
    {
        var response = await request.PostAsync($"/api/admin/menus/{menuKey}/items", new()
        {
            DataObject = new
            {
                parentId = (Guid?)null,
                sortOrder = 0,
                displayName,
                icon = (string?)null,
                itemType,
                config = JsonSerializer.SerializeToElement(new { }),
                permissionRequired = (string?)null,
                isVisible = true
            }
        });
        Assert.True(response.Ok, await response.TextAsync());
        var json = await response.JsonAsync();
        return json!.Value.GetProperty("id").GetGuid();
    }
}
