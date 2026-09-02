using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class AdminOperationsTests : E2ETestBase
{
    public AdminOperationsTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ExternalConnections_CreateEditTestDisableAndDelete()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("connection");
        var renamed = TestNames.Prefixed("connection-edited");

        await page.GotoAsync("/admin/config/external-connections");
        await page.GetByRole(AriaRole.Button, new() { Name = "New connection" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "New connection" });
        await dialog.GetByLabel("Name").FillAsync(name);
        await dialog.GetByLabel("API key").FillAsync("e2e-not-a-real-key");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(page.GetByText(name, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = $"Edit {name}" }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit connection" });
        await dialog.GetByLabel("Name").FillAsync(renamed);
        await dialog.GetByLabel("Enabled").UncheckAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Assertions.Expect(page.GetByText(renamed, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText("Disabled", new() { Exact = true }))
            .ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = $"Test {renamed}" }).ClickAsync();
        await Assertions.Expect(page.GetByText(new System.Text.RegularExpressions.Regex("^(OK|Error):?")))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = $"Delete {renamed}" }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Delete connection" });
        await dialog.GetByText("Delete", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(renamed, new() { Exact = true }).First)
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Projections_PauseResumeAndRequestRebuild()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/projections");
        var firstRow = page.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Pause" }) }).First;
        await Assertions.Expect(firstRow).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Pause" }).ClickAsync();
        firstRow = page.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Resume" }) }).First;
        await Assertions.Expect(firstRow.GetByText("Paused", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Resume" }).ClickAsync();
        firstRow = page.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Pause" }) }).First;
        await Assertions.Expect(firstRow.GetByText("Running", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Rebuild" }).ClickAsync();
        await Assertions.Expect(firstRow.GetByRole(AriaRole.Button, new() { Name = "Rebuild" }))
            .ToBeEnabledAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Plugins_UploadEnableDisableUpdateAndDelete()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var archive = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../plugins/HelloPlugin/dist/HelloPlugin.zip"));

        await page.GotoAsync("/admin/config/plugins");
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload plugin" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload plugin" });
        await dialog.Locator("input[type=file]").SetInputFilesAsync(archive);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "HelloPlugin" });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Enable" }).ClickAsync();
        await Assertions.Expect(row.GetByText("Enabled", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();
        await Assertions.Expect(row.GetByText("Disabled", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await row.GetByRole(AriaRole.Button, new() { Name = "Update" }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Update plugin: HelloPlugin" });
        await dialog.Locator("input[type=file]").SetInputFilesAsync(archive);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Update" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 20_000 });

        Task? acceptDialogTask = null;
        page.Dialog += (_, browserDialog) => acceptDialogTask = browserDialog.AcceptAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(row).Not.ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Appearance_SiteNamePersistsAcrossReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/appearance");
        var siteName = page.GetByLabel("Site name");
        await Assertions.Expect(siteName).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var original = await siteName.InputValueAsync();
        var changed = TestNames.Prefixed("site-name");

        try
        {
            await siteName.FillAsync(changed);
            await SaveAppearanceAsync(page);
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByLabel("Site name")).ToHaveValueAsync(changed);
        }
        finally
        {
            await page.GotoAsync("/admin/config/appearance");
            siteName = page.GetByLabel("Site name");
            if (await siteName.InputValueAsync() != original)
            {
                await siteName.FillAsync(original);
                await SaveAppearanceAsync(page);
            }
        }
    }

    [Fact]
    public async Task Appearance_ShippedDefaults_RaiseNoContrastAdvisory()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/appearance");
        await Assertions.Expect(page.GetByLabel("Site name"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The gate archived-40 asks for, aimed at the values a real install actually
        // gets rather than at the SPA constant.
        //
        // Two accessible defaults had been corrected in the SPA's
        // DEFAULT_SITE_APPEARANCE and never mirrored into the server-side seed
        // that CreateDefaultEntity writes — so a fresh install still shipped a
        // 2.07:1 sidebar heading and a 2.80:1 accent while the constant said
        // otherwise. A check against the constant would have passed happily.
        // This one reads the editor's own advisory, which is computed from
        // whatever the database returned.
        await Assertions.Expect(page.GetByText("Accessibility advisory"))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Appearance_PrimaryButtonText_TakesTheReadableColour()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/appearance");

        // Exact, by role: the eyedropper button beside this field is named
        // "Pick primary accent color from screen", which GetByLabel's
        // substring match also picks up.
        var accent = page.GetByRole(AriaRole.Textbox, new() { Name = "Primary accent", Exact = true });
        await Assertions.Expect(accent).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // #00acac is the case that motivated archived-14: the old YIQ heuristic
        // thresholded brightness at 160 and chose white, which computes to
        // 2.80:1. Black is 6.74:1 on the same colour. The value feeds
        // --mantine-primary-color-contrast, so it is the text colour of every
        // filled primary button, not just status pills.
        await accent.FillAsync("#00acac");

        // The editor applies a live preview, so the token updates without a
        // save — no need to mutate the site's real appearance to assert this.
        await Assertions.Expect(page.Locator(":root")).ToBeVisibleAsync();
        var contrastToken = await PollContrastTokenAsync(page, "#111111");
        Assert.Equal("#111111", contrastToken);
    }

    // The token is written by an effect after the draft updates, so read it
    // until it settles rather than assuming the first paint has it.
    private static async Task<string> PollContrastTokenAsync(IPage page, string expected)
    {
        var last = string.Empty;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            last = (await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.documentElement)" +
                ".getPropertyValue('--mantine-primary-color-contrast').trim()")) ?? string.Empty;
            if (string.Equals(last, expected, StringComparison.OrdinalIgnoreCase)) return last;
            await page.WaitForTimeoutAsync(250);
        }
        return last;
    }

    // Clicking "Save changes" only dispatches the PATCH. Reloading straight
    // after the click races it: the navigation aborts the in-flight request
    // and the value never reaches the database, which reads exactly like a
    // silent save-and-revert (archived-172 was filed on that reading). Wait for the
    // page's own success alert, which is the completion signal a user waits
    // for too, so the spec also now asserts that saving reports success.
    private static async Task SaveAppearanceAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Appearance settings saved."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
