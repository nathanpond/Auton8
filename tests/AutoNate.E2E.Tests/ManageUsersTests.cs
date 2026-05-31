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

    [Fact]
    public async Task ManageUsers_ResetPassword_AllowsLoginWithNewPassword()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var username = $"e2euser_{TestNames.ShortSlug()}";
        const string originalPassword = "P@ssword123!";
        const string newPassword = "N3wP@ssword456!";

        await page.GotoAsync("/manage-users");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add user" }).ClickAsync();
        var addUserDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add User" });
        await addUserDialog.GetByLabel("Username").FillAsync(username);
        await addUserDialog.GetByLabel("First Name").FillAsync("E2E");
        await addUserDialog.GetByLabel("Last Name").FillAsync("Reset");
        await addUserDialog.GetByLabel("Email").FillAsync($"{username}@e2e.local");
        await addUserDialog.GetByLabel("Password").FillAsync(originalPassword);
        await addUserDialog.GetByRole(AriaRole.Button, new() { Name = "Add User" }).ClickAsync();
        await Assertions.Expect(addUserDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = $"Reset password for {username}" })
            .ClickAsync();
        var resetDialog = page.GetByRole(AriaRole.Dialog,
            new() { Name = $"Reset password for {username}" });
        await resetDialog.GetByLabel("New password").FillAsync(newPassword);
        await resetDialog.GetByRole(AriaRole.Button, new() { Name = "Reset password" }).ClickAsync();
        await Assertions.Expect(resetDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Admin User" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Logout" }).ClickAsync();
        await page.WaitForURLAsync("**/", new() { Timeout = 10_000 });

        await AutoNateE2EFixture.SignInAsync(page, username, newPassword);
        Assert.Matches("/home", page.Url);
        var meResponse = await page.APIRequest.GetAsync("/api/auth/me");
        var me = await meResponse.JsonAsync();
        Assert.Equal(username, me!.Value.GetProperty("username").GetString());
    }

    [Fact]
    public async Task ManageUsers_EditAndDeleteUser_UpdatesTheList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var username = $"e2euser_{TestNames.ShortSlug()}";

        await page.GotoAsync("/manage-users");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add user" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Add User" });
        await dialog.GetByLabel("Username").FillAsync(username);
        await dialog.GetByLabel("First Name").FillAsync("Before");
        await dialog.GetByLabel("Last Name").FillAsync("Edit");
        await dialog.GetByLabel("Email").FillAsync($"{username}@e2e.local");
        await dialog.GetByLabel("Password").FillAsync("P@ssword123!");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add User" }).ClickAsync();
        await Assertions.Expect(page.GetByText(username, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByText(username, new() { Exact = true }).First.ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Edit {username}" });
        await dialog.GetByLabel("First Name").FillAsync("After");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(page.GetByText("After Edit", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = $"Delete {username}" }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Delete {username}?" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(username, new() { Exact = true }).First)
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
