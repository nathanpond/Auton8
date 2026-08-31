using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 1 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>): auth and shell
/// behavior that every other page sits on top of — logout via the user menu,
/// the protected-route redirect for unauthenticated users, the main nav
/// rendering for the seeded admin, the 404 page for unknown routes, and
/// session-cookie persistence across a hard reload.
/// </summary>
public sealed class AuthShellTests : E2ETestBase
{
    public AuthShellTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task UserMenu_Logout_ReturnsToLoginAndClearsSession()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // The user-menu trigger is an UnstyledButton whose accessible name
        // is the user's display name ("Admin User" for the seeded admin —
        // first_name + last_name from local_users).
        await page.GetByRole(AriaRole.Button, new() { Name = "Admin User" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Logout" }).ClickAsync();

        // The form-based logout POSTs to /account/logout which 302s back to /.
        await page.WaitForURLAsync("**/", new() { Timeout = 10_000 });

        // /api/auth/me must now report unauthenticated — if the cookie was
        // still good a regression in cookie clearing would let the dashboard
        // come back on the next navigation.
        var meResponse = await page.APIRequest.GetAsync("/api/auth/me");
        var me = await meResponse.JsonAsync();
        Assert.False(me!.Value.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task UnauthenticatedAccessToProtectedRoute_RedirectsToLoginWithReturnUrl()
    {
        // Fresh context, no sign-in: hits ProtectedRoute which renders a
        // <Navigate to="/?returnUrl=…"/>.
        await using var session = await NewAnonymousSessionAsync();
        var page = session.Page;

        await page.GotoAsync("/workflow-executions");

        // Lands on / with the original path preserved in `returnUrl` so the
        // login flow can bounce back after sign-in.
        await page.WaitForURLAsync("**/?returnUrl=%2Fworkflow-executions",
            new() { Timeout = 10_000 });

        // And the login form is what's actually rendered (proves we got the
        // Login page, not just a 302).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Sign me in" }))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Fact]
    public async Task NavMenu_ForAdmin_RendersExpectedTopLevelGroupsAndRoutes()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // Top-level main-menu items seeded by DatabaseSchemaInitializer's
        // initial main-menu block + later additions (Documents,
        // DocumentsMenuItemSeedSql; Query, query_menu_v1; Data,
        // main_menu_data_v1). Use a header role filter so we don't collide
        // with the same words appearing on the dashboard body.
        var header = page.GetByRole(AriaRole.Banner);
        await Assertions.Expect(header.GetByText("Dashboard", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(header.GetByText("Records", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(header.GetByText("Workflows", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(header.GetByText("Documents", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(header.GetByText("Query", new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(header.GetByText("Data", new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task UnknownRoute_RendersNotFoundPage()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/this-route-definitely-does-not-exist");

        // NotFound.tsx renders a "404" heading and a "Go Home" link back to
        // /home.
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "404" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Go Home" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Session_PersistsAcrossHardReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // We should be on /home after sign-in; reload and confirm we stay
        // there (no kick to / + ?returnUrl). Heading proves the dashboard
        // re-rendered with an authenticated session, not just that the URL
        // didn't move.
        Assert.Matches("/home", page.Url);
        await page.ReloadAsync();
        Assert.Matches("/home", page.Url);

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Automation Dashboard" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var meResponse = await page.APIRequest.GetAsync("/api/auth/me");
        var me = await meResponse.JsonAsync();
        Assert.True(me!.Value.GetProperty("authenticated").GetBoolean());
        Assert.Equal("admin", me.Value.GetProperty("username").GetString());
    }

    [Fact]
    public async Task UnauthenticatedApiAuthMe_ReportsUnauthenticated()
    {
        // Belt-and-braces for the protected-route test above: even without
        // navigating to a protected route, a fresh-cookie-jar /api/auth/me
        // call must report authenticated=false. Catches regressions where
        // the server might mistakenly issue an auth cookie pre-login (the
        // exact scenario the BadPassword case in LoginTests guards against,
        // but from the opposite direction — no login attempt at all).
        await using var context = await Fixture.NewContextAsync();

        var response = await context.APIRequest.GetAsync("/api/auth/me");
        Assert.True(response.Ok);
        var json = await response.JsonAsync();
        Assert.False(json!.Value.GetProperty("authenticated").GetBoolean());
    }
}
