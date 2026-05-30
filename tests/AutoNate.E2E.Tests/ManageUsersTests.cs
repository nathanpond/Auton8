using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 7 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — the real
/// Manage Users page (mounted at <c>/manage-users</c> via the icon menu's
/// <c>manageUsers</c> template; the <c>/admin/config/users</c> route renders
/// a stub and is intentionally not exercised here).
/// </summary>
public sealed class ManageUsersTests : E2ETestBase
{
    public ManageUsersTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ManageUsers_RendersAddUserAffordance()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/manage-users");

        // The "Add user" ActionIcon (aria-label="Add user", ManageUsers.tsx:241)
        // is the toolbar entry point that opens the Add User modal — its
        // presence proves the page mounted and the user list resolved.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Add user" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The seeded admin row should always appear; assert by username text.
        await Assertions.Expect(page.GetByText("admin", new() { Exact = true }).First)
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task ManageUsers_AddUser_AppearsInTheList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/manage-users");

        await page.GetByRole(AriaRole.Button, new() { Name = "Add user" }).ClickAsync();

        // Modal title "Add User" (ManageUsers.tsx:360).
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var username = $"e2euser_{TestNames.ShortSlug()}";
        await modal.GetByLabel("Username").FillAsync(username);
        await modal.GetByLabel("First Name").FillAsync("E2E");
        await modal.GetByLabel("Last Name").FillAsync("User");
        await modal.GetByLabel("Email").FillAsync($"{username}@e2e.local");
        await modal.GetByLabel("Password").FillAsync("P@ssword123!");

        // Submit button label is "Add User" (same as modal title). Constrain
        // to the modal's tree to avoid the toolbar's `Add user` ActionIcon.
        await modal.GetByRole(AriaRole.Button, new() { Name = "Add User" }).ClickAsync();

        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(username).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task ManageUsers_AdminRow_ExposesResetPasswordAffordance()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/manage-users");

        // Each row's reset-password ActionIcon has aria-label="Reset password
        // for {username}" (ManageUsers.tsx:154). Asserting it for the seeded
        // admin row proves both the row rendered and the per-row controls
        // are wired up.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Reset password for admin" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
