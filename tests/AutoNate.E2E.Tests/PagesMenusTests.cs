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

    /// <summary>
    /// A rename survives navigating away the instant the dialog closes (#227).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deterministic form of the flake. <c>applyEdit</c> used to dismiss the
    /// modal and update the list optimistically <em>before</em> sending the PUT,
    /// so "the dialog closed" meant "the request is in flight", not "the rename
    /// is saved". Navigating immediately aborted it, and the rename was lost
    /// with no error anywhere — the failure handler sets in-page state, and an
    /// aborted request has no page left to show it on.
    /// </para>
    /// <para>
    /// The delay is what makes it a test rather than a coin toss: under load the
    /// real PUT is slow enough to lose the race, which is why this only ever
    /// failed in a full run. Held at 1.5s deliberately — long enough that an
    /// optimistic close always loses, short enough to cost nothing.
    /// </para>
    /// <para>
    /// Asserted against the SERVER, not the list. The list is the thing that was
    /// lying: it showed the new name from local state while the write was gone.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RenamingAMenuItem_SurvivesNavigatingAwayWhileTheSaveIsSlow()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("slow-save");
        var renamed = TestNames.Prefixed("slow-save-edited");
        var path = $"/e2e-slow-{TestNames.ShortSlug()}";

        await CreateStandaloneItemAsync(page.APIRequest, name, "page", new
        {
            path,
            contentType = "html",
            content = "<h2>slow</h2>"
        });

        // PATCH /api/admin/menus/items/{id} -- the update route is NOT under the
        // menu key, unlike the create. Getting either half wrong makes this test
        // pass for the wrong reason: the route never matches, no delay happens,
        // and the race is a coin toss again.
        await page.RouteAsync("**/api/admin/menus/items/**", async route =>
        {
            if (route.Request.Method == "PATCH") await Task.Delay(1_500);
            await route.ContinueAsync();
        });

        await OpenStandaloneMenuAsync(page);
        var row = MenuRow(page, name);
        await row.GetByRole(AriaRole.Button, new() { Name = "Edit item" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit menu item" });
        await dialog.GetByLabel("Display name").FillAsync(renamed);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save and Close" }).ClickAsync();

        // The contract: the dialog closing means the write is DONE, not sent.
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Leave immediately. This is the move that used to destroy the write.
        await page.GotoAsync(path);

        var stored = await page.APIRequest.GetAsync("/api/admin/menus/standalone");
        Assert.True(stored.Ok, await stored.TextAsync());

        var body = await stored.TextAsync();
        Assert.True(
            body.Contains(renamed, StringComparison.Ordinal),
            $"The rename to '{renamed}' never reached the server. The dialog had closed and the "
            + "list showed the new name from local state, so the UI reported a save that was "
            + "aborted by the navigation (#227). Stored items:\n" + body[..Math.Min(1200, body.Length)]);
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
