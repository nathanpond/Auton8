using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 7 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — the real
/// IAM admin pages mounted via the icon menu's <c>adminRoles</c> /
/// <c>adminGroups</c> / <c>adminGrants</c> templates (the
/// <c>/admin/config/...</c> counterparts are stubs). Roles and Groups have
/// nearly identical create-shaped UIs (one TextInput + a Create button);
/// Grants is more complex (Mantine Select chain + a SelectorBuilder), so
/// we cap that at a smoke that the Add-grant form mounts — actual grant
/// creation by clicking through Mantine Selects is brittle, and the API-level
/// grant authorization is exhaustively covered in
/// <c>AutoNate.Web.Tests/Authorization/</c>.
/// </summary>
public sealed class IamAdminTests : E2ETestBase
{
    public IamAdminTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AdminRoles_CreateRole_AppearsInTheList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/roles");

        // PageHeader title="Roles" (Roles.tsx:58). Exact=true — the page
        // also renders an h5 "All roles" in the side card, which would
        // otherwise hit Playwright's strict-mode multiple-match guard.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Roles", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var roleName = TestNames.Prefixed("role");
        await page.GetByPlaceholder("New role name").FillAsync(roleName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(page.GetByText(roleName).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task AdminGroups_CreateGroup_AppearsInTheList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/groups");

        // PageHeader title="Groups" (Groups.tsx:52). Exact=true for the
        // same h1/h5 substring-collision reason as Roles above.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Groups", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var groupName = TestNames.Prefixed("grp");
        await page.GetByPlaceholder("New group name").FillAsync(groupName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(page.GetByText(groupName).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task AdminGrants_PageMountsWithAddGrantForm()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/grants");

        // PageHeader title="Permissions" (Grants.tsx:175).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Permissions" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The "Add grant" card heading + the "How permissions work" help
        // anchor together prove both the inputs panel and the help wiring
        // mounted (Grants.tsx:194, 202).
        await Assertions.Expect(page.GetByText("Add grant")).ToBeVisibleAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "How permissions work" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task AdminGrants_CreateAndRevokeGrant_UpdatesTable()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var username = TestNames.Prefixed("grant-user");
        await seeder.CreateUserAsync(username, "Password123!");

        await page.GotoAsync("/admin/grants");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Principal", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = username, Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = username });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Task? acceptDialogTask = null;
        page.Dialog += (_, dialog) => acceptDialogTask = dialog.AcceptAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke grant" }).ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(row).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task PermissionChecker_ShowsAllowAndDenyVerdicts()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var allowedUser = await seeder.CreateUserAsync(TestNames.Prefixed("checker-allow"), "Password123!");
        var deniedUser = await seeder.CreateUserAsync(TestNames.Prefixed("checker-deny"), "Password123!");
        await seeder.GrantAsync("user", allowedUser.UserId, "view", "/record/*");
        var explainResponse = await page.APIRequest.PostAsync("/api/admin/explain/", new()
        {
            DataObject = new
            {
                asUserId = allowedUser.UserId,
                action = "view",
                targetKind = "record",
                targetId = (string?)null
            }
        });
        Assert.True(explainResponse.Ok, await explainResponse.TextAsync());
        var explainJson = await explainResponse.JsonAsync();
        Assert.Equal("allow", explainJson!.Value.GetProperty("effect").GetString());

        await page.GotoAsync("/admin/explain");
        await ExplainForUserAsync(page, allowedUser.Username);
        await Assertions.Expect(page.GetByText("allow", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await ExplainForUserAsync(page, deniedUser.Username);
        await Assertions.Expect(page.GetByText("deny", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task AdminRoles_AssignmentPersistsAcrossReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var roleName = TestNames.Prefixed("assigned-role");
        var user = await seeder.CreateUserAsync(TestNames.Prefixed("role-user"), "Password123!");

        await page.GotoAsync("/admin/roles");
        await page.GetByPlaceholder("New role name").FillAsync(roleName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await page.GetByText(roleName, new() { Exact = true }).ClickAsync();
        await page.GetByPlaceholder("— pick user —").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = user.Username, Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = user.Username, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.ReloadAsync();
        await page.GetByText(roleName, new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = user.Username, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private static async Task ExplainForUserAsync(IPage page, string username)
    {
        await page.GetByRole(AriaRole.Combobox, new() { Name = "User" }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = username, Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();
    }
}
