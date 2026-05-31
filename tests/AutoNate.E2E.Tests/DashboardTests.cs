using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Covers the dynamic <c>/dashboard</c> page's user-owned dashboard lifecycle.
/// </summary>
public sealed class DashboardTests : E2ETestBase
{
    public DashboardTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DashboardPage_CreateRenameAndDeleteDashboard()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var dashboardName = TestNames.Prefixed("dashboard");
        var renamedDashboard = TestNames.Prefixed("dashboard-renamed");
        var mountPath = $"/dashboard-{TestNames.ShortSlug()}";
        var seeder = new ApiSeeder(page.APIRequest);
        await seeder.CreateDashboardMountAsync(mountPath);

        await page.GotoAsync(mountPath);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
                new() { Name = "Dashboard", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Dashboard" });
        await Assertions.Expect(selector).ToHaveValueAsync("My Dashboard",
            new() { Timeout = 15_000 });

        Task? acceptDialogTask = null;
        page.Dialog += (_, dialog) => acceptDialogTask = dialog.AcceptAsync(dashboardName);
        await page.GetByRole(AriaRole.Button, new() { Name = "New dashboard" }).ClickAsync();
        await (acceptDialogTask ?? throw new InvalidOperationException("New dashboard prompt did not open."));
        await Assertions.Expect(selector).ToHaveValueAsync(dashboardName,
            new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Dashboard actions" }).ClickAsync();
        await page.GetByText(new System.Text.RegularExpressions.Regex("^Rename")).ClickAsync();
        var renameDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Rename dashboard" });
        await renameDialog.GetByLabel("Name").FillAsync(renamedDashboard);
        await renameDialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(selector).ToHaveValueAsync(renamedDashboard,
            new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Dashboard actions" }).ClickAsync();
        await page.GetByText(new System.Text.RegularExpressions.Regex("^Delete")).ClickAsync();
        var deleteDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Delete dashboard?" });
        await deleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await Assertions.Expect(selector).ToHaveValueAsync("My Dashboard",
            new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task DashboardPage_AddConfigureAndRemoveWidget()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var mountPath = $"/dashboard-{TestNames.ShortSlug()}";
        var seeder = new ApiSeeder(page.APIRequest);
        await seeder.CreateDashboardMountAsync(mountPath);

        await page.GotoAsync(mountPath);
        await Assertions.Expect(page.GetByText("This dashboard is empty."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Add your first widget" }).ClickAsync();
        var picker = page.GetByRole(AriaRole.Dialog, new() { Name = "Add a widget" });
        await picker.GetByLabel("Search widgets").FillAsync("Data table");
        await picker.GetByRole(AriaRole.Button).Filter(new() { HasText = "Data table" }).ClickAsync();
        await picker.GetByRole(AriaRole.Button, new() { Name = "Add widget" }).ClickAsync();

        var drawer = page.GetByRole(AriaRole.Dialog, new() { Name = "Configure: Data table" });
        await Assertions.Expect(drawer).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await drawer.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(drawer).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Assertions.Expect(page.Locator(".mantine-Drawer-overlay"))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.Locator(".widget-frame").HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove widget" }).ClickAsync();
        var removeDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Remove widget?" });
        await removeDialog.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();

        await Assertions.Expect(removeDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText("This dashboard is empty."))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
