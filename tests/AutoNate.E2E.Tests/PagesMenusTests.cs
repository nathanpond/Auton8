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
        // COUNTED, not assumed (#489). The first draft of this test used the
        // wrong verb AND the wrong path, so nothing was delayed, the race was a
        // coin toss, and it failed on an unrelated assertion with an empty
        // message. Had it failed one line later it would have "proved" the bug
        // while testing nothing. If the endpoint ever moves, this fails loudly
        // instead of going quietly green.
        var intercepted = 0;
        await page.RouteAsync("**/api/admin/menus/items/**", async route =>
        {
            if (route.Request.Method == "PATCH")
            {
                Interlocked.Increment(ref intercepted);
                await Task.Delay(1_500);
            }
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

        Assert.True(
            intercepted >= 1,
            "The PATCH was never intercepted, so nothing was delayed and this test proved "
            + "nothing about the race. The update route is PATCH /api/admin/menus/items/{id} "
            + "-- check the glob and the method before reading any other assertion (#489).");

        var stored = await page.APIRequest.GetAsync("/api/admin/menus/standalone");
        Assert.True(stored.Ok, await stored.TextAsync());

        var body = await stored.TextAsync();
        Assert.True(
            body.Contains(renamed, StringComparison.Ordinal),
            $"The rename to '{renamed}' never reached the server. The dialog had closed and the "
            + "list showed the new name from local state, so the UI reported a save that was "
            + "aborted by the navigation (#227). Stored items:\n" + body[..Math.Min(1200, body.Length)]);
    }

    /// <summary>
    /// A refused save keeps the draft and does not leave a lie in the tree (#489).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #227 closed the <em>abort</em> path — navigating away no longer destroys
    /// the write. The <em>rejection</em> path told the same lie one step over:
    /// <c>handleEditItem</c> swallowed the error and returned normally, so the
    /// editor closed the dialog, discarded what the user had typed, and kept its
    /// optimistic name in the list. The page showed a red banner above a tree
    /// still displaying a name the server had refused.
    /// </para>
    /// <para>
    /// Asserted from the list AND the server, because the list is the thing that
    /// was lying.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusedRenameKeepsTheDialogOpenAndDoesNotChangeTheTree()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("refused-save");
        var renamed = TestNames.Prefixed("refused-save-edited");
        var path = $"/e2e-refused-{TestNames.ShortSlug()}";

        await CreateStandaloneItemAsync(page.APIRequest, name, "page", new
        {
            path,
            contentType = "html",
            content = "<h2>refused</h2>"
        });

        var refused = 0;
        await page.RouteAsync("**/api/admin/menus/items/**", async route =>
        {
            if (route.Request.Method == "PATCH")
            {
                Interlocked.Increment(ref refused);
                await route.FulfillAsync(new() { Status = 500, Body = "nope" });
                return;
            }
            await route.ContinueAsync();
        });

        await OpenStandaloneMenuAsync(page);
        await MenuRow(page, name).GetByRole(AriaRole.Button, new() { Name = "Edit item" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit menu item" });
        await dialog.GetByLabel("Display name").FillAsync(renamed);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save and Close" }).ClickAsync();

        // THE BANNER IS THE BARRIER (#495). Asserting the dialog visible straight
        // after the click had nothing to wait on: in a BROKEN build the close
        // lands only after the 500 round-trip, so Playwright could sample
        // visibility first and the bug would flake the test GREEN. Waiting for
        // the error the refusal produces fixes the race and asserts the
        // complement the test was missing -- delete `setError` from the catch and
        // this fails, where before every test stayed green.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert, new() { Name = "Error" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.True(refused >= 1, "the PATCH was never intercepted, so nothing was refused");

        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The draft survives: closing on a refusal is how the first version lost work.
        Assert.Equal(renamed, await dialog.GetByLabel("Display name").InputValueAsync());

        // And the tree does not show a name the server rejected.
        await Assertions.Expect(MenuRow(page, renamed)).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        var stored = await page.APIRequest.GetAsync("/api/admin/menus/standalone");
        Assert.True(stored.Ok, await stored.TextAsync());

        var body = await stored.TextAsync();
        Assert.DoesNotContain(renamed, body, StringComparison.Ordinal);
        Assert.Contains(name, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused visibility toggle reverts the badge (#495).
    /// </summary>
    /// <remarks>
    /// <c>toggleVisible</c>'s rollback shipped with #489 and nothing exercised
    /// it — remove the <c>try/catch</c> and the whole suite stayed green. The
    /// badge renders from local state, so without the rollback the row claims a
    /// visibility the server refused.
    /// </remarks>
    [Fact]
    public async Task ARefusedVisibilityToggleRevertsTheBadge()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("refused-toggle");

        await CreateStandaloneItemAsync(page.APIRequest, name, "page", new
        {
            path = $"/e2e-toggle-{TestNames.ShortSlug()}",
            contentType = "html",
            content = "<h2>t</h2>"
        });

        var refused = 0;
        await page.RouteAsync("**/api/admin/menus/items/**", async route =>
        {
            if (route.Request.Method == "PATCH")
            {
                Interlocked.Increment(ref refused);
                await route.FulfillAsync(new() { Status = 500, Body = "nope" });
                return;
            }
            await route.ContinueAsync();
        });

        await OpenStandaloneMenuAsync(page);
        var row = MenuRow(page, name);
        await row.GetByRole(AriaRole.Button, new() { Name = "Toggle visibility" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Alert, new() { Name = "Error" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.True(refused >= 1, "the PATCH was never intercepted, so nothing was refused");

        // The badge must be back to visible -- it renders from local state, which
        // is exactly why an un-rolled-back optimistic update lies here.
        await Assertions.Expect(row.GetByText("hidden", new() { Exact = true }))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // THE ROW UNDER TEST, not the payload (#499). This was
        // `Assert.Contains("\"isVisible\":true", <whole payload>)`, and six tests
        // in this class create standalone items that nobody deletes -- so it
        // passed on somebody else's row whatever happened to this one.
        var stored = await page.APIRequest.GetAsync("/api/admin/menus/standalone");
        Assert.True(stored.Ok, await stored.TextAsync());

        using var menu = JsonDocument.Parse(await stored.TextAsync());
        var mine = menu.RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("displayName").GetString() == name);

        Assert.True(mine.GetProperty("isVisible").GetBoolean(),
            $"The server stored isVisible=false for '{name}' even though the PATCH was refused.");
    }

    /// <summary>
    /// A refused delete puts the row back, and says so (#495).
    /// </summary>
    /// <remarks>
    /// The delete path was `void deleteItem.mutateAsync(id)` — fire and forget.
    /// On a refusal the row stayed gone (the hook invalidates on success only),
    /// <b>no banner appeared at all</b>, and the rejection surfaced as an
    /// unhandled promise. Worse than the rename bug #489 fixed, where the user
    /// at least saw red. And `onChange` had already marked the tree dirty with a
    /// live item missing, so "Save order" would post a list that omits it.
    /// </remarks>
    [Fact]
    public async Task ARefusedDeletePutsTheRowBackAndReportsIt()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("refused-delete");

        await CreateStandaloneItemAsync(page.APIRequest, name, "page", new
        {
            path = $"/e2e-del-{TestNames.ShortSlug()}",
            contentType = "html",
            content = "<h2>d</h2>"
        });

        // The id, so the PUT body can be checked for it by identity rather than
        // by display name -- the tree request carries ids only.
        var listed = await page.APIRequest.GetAsync("/api/admin/menus/standalone");
        Assert.True(listed.Ok, await listed.TextAsync());
        using var listedDoc = JsonDocument.Parse(await listed.TextAsync());
        var itemId = listedDoc.RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("displayName").GetString() == name)
            .GetProperty("id").GetString();

        var refused = 0;
        await page.RouteAsync("**/api/admin/menus/items/**", async route =>
        {
            if (route.Request.Method == "DELETE")
            {
                Interlocked.Increment(ref refused);
                await route.FulfillAsync(new() { Status = 500, Body = "nope" });
                return;
            }
            await route.ContinueAsync();
        });

        await OpenStandaloneMenuAsync(page);
        Task? accepted = null;
        page.Dialog += (_, d) => accepted = d.AcceptAsync();
        await MenuRow(page, name).GetByRole(AriaRole.Button, new() { Name = "Delete item" }).ClickAsync();
        if (accepted is not null) await accepted;

        await Assertions.Expect(page.GetByRole(AriaRole.Alert, new() { Name = "Error" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.True(refused >= 1, "the DELETE was never intercepted, so nothing was refused");

        // The row is back in the tree, not silently gone.
        await Assertions.Expect(MenuRow(page, name))
            .ToHaveCountAsync(1, new() { Timeout = 10_000 });

        // THE OTHER HALF OF THE ROLLBACK (#499). `setItems` puts the row back in
        // the tree; `onChange` puts it back in `pendingItems`. Without the
        // second, every assertion above still holds -- row back, banner up,
        // server intact -- while `pendingItems` still holds the post-delete list.
        // The harm is specific: pressing "Save order" then PUTs a node list that
        // omits a live item.
        //
        // So assert the harm, not the dirty flag. `isStructurallyDirty` compares
        // the editor's reindexed sortOrder against the server's stored values,
        // which are all 0 for items this suite creates -- so the tree reads
        // dirty here for reasons that have nothing to do with the delete, and an
        // assertion on the button would fail for the wrong reason (#503).
        //
        // Measured before this existed: deleting only the `onChange` line left
        // this test green.
        string? savedTree = null;
        await page.RouteAsync("**/api/admin/menus/**/tree", async route =>
        {
            savedTree = route.Request.PostData;
            await route.FulfillAsync(new() { Status = 200, Body = "{}" });
        });

        var saveOrder = page.GetByRole(AriaRole.Button, new() { Name = "Save order" });
        if (await saveOrder.CountAsync() > 0)
        {
            await saveOrder.ClickAsync();
            await Assertions.Expect(saveOrder).ToHaveCountAsync(0, new() { Timeout = 10_000 });

            Assert.True(savedTree is not null, "Save order sent no tree request.");
            Assert.Contains(itemId!, savedTree!, StringComparison.Ordinal);
        }

        var stored = await page.APIRequest.GetAsync("/api/admin/menus/standalone");
        Assert.True(stored.Ok, await stored.TextAsync());
        Assert.Contains(name, await stored.TextAsync(), StringComparison.Ordinal);
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
