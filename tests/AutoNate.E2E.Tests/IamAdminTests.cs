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
}
