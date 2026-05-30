using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 9 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — misc render
/// + no-error-banner smoke for surfaces that don't fit the earlier
/// per-domain test classes: notifications, user profile, bus watcher, and
/// two admin diagnostic pages (Hierarchy, Effective Permissions).
///
/// The plan's original "dashboard mounts" bullet is substituted with
/// <c>/admin/explain</c> here: the configurable dashboard is reachable
/// only when an admin places its template on a menu, and the seeded
/// menu tree in <c>AutoNate_E2E</c> doesn't include one, so the route
/// would 404. Hierarchy + Explain are both wired through the icon menu
/// and exist in every fresh DB.
/// </summary>
public sealed class MiscPagesTests : E2ETestBase
{
    public MiscPagesTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Notifications_RendersHeadingAndMarkAllReadButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/notifications");

        // Notifications.tsx:109 — PageHeader title="Notifications".
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Notifications" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The toolbar-right button is "Mark all read" (Notifications.tsx:135).
        // It mounts even when the inbox is empty (the mutation is enabled
        // unconditionally and Mantine Buttons render disabled when needed),
        // so this assertion holds on the fresh test DB.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Mark all read" }))
            .ToBeVisibleAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task UserProfile_RendersHeadingAndAdminDisplayName()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/user-profile");

        // PageHeader title="User Profile" (UserProfile.tsx:38).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "User Profile" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The seeded admin row has first_name="Admin" + last_name="User"
        // (infra/postgres/init/02-...sql), so the page renders an h3
        // "Admin User" display name (UserProfile.tsx:49-50).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Admin User" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task BusWatcher_RendersHeadingForSuperAdmin()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // Bus Watcher's standalone /bus-watcher URL was retired; the
        // `site_config_site_information_v1` migration moved the menu item
        // into the Site Configuration "Site Information" group, where it
        // mounts at /admin/config/bus-watcher inside ConfigLayout.
        await page.GotoAsync("/admin/config/bus-watcher");

        // BusWatcher.tsx:85 — PageHeader title="Bus Watcher" (the SuperAdmin
        // path). The non-admin path shows the same heading but follows it
        // with a "restricted to SuperAdmins" notice — admin avoids the gate.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Bus Watcher" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The live-stream log uses aria-label="Bus event log"
        // (BusWatcher.tsx:110) — proves the SuperAdmin branch took.
        await Assertions.Expect(page.GetByLabel("Bus event log"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Hierarchy_PageMounts()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/hierarchy");

        // Hierarchy.tsx:145 — PageHeader title="Hierarchy".
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Hierarchy" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AdminExplain_PageMounts()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/explain");

        // Explain.tsx:85 — PageHeader title="Effective Permissions".
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Effective Permissions" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }
}
