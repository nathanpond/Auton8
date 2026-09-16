using System.Linq;
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

    // archived-18: only four admin datastore pages set a document title; every other

    // tab, history entry and window-switcher row read the bare site name, so

    // screen-reader users navigating between pages had nothing to orient by

    // (WCAG 2.4.2 / 508 §502). Titles now come from APP_ROUTES centrally.

    [Fact]

    public async Task Navigation_sets_a_distinct_document_title_per_route()

    {

        await using var session = await NewSignedInAsAdminAsync();

        var page = session.Page;


        var seen = new List<string>();

        foreach (var (path, expected) in new[]

                 {

                     ("/workflow-executions", "Workflow Executions"),

                     ("/notifications", "Notifications"),

                     ("/projects", "Projects"),

                     ("/query", "Query"),

                 })

        {

            await page.GotoAsync(path);

            await Assertions.Expect(page).ToHaveTitleAsync(

                new System.Text.RegularExpressions.Regex(expected),

                new() { Timeout = 15_000 });

            seen.Add(await page.TitleAsync());

        }


        // Distinct titles are the point — four routes that all read the same

        // thing would satisfy a per-route assertion but not a screen reader.

        Assert.Equal(seen.Count, seen.Distinct().Count());

    }


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

    // The app under test runs under a real Dapr sidecar in full-local (#487):
    // `make test-full-local` sets AUTONATE_E2E_DAPR=1 and the fixture wraps the
    // host in `dapr run` WITHOUT AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR -- so
    // Program.cs's startup probe would have thrown if no sidecar answered.
    //
    // That makes the boot itself the guarantee. The assertion below still checks
    // it explicitly, because a trait whose promise is only implied by the harness
    // is the shape this spec had before: it asserted a heading that renders with
    // no sidecar at all, so RequiresService=Dapr meant nothing.
    [Trait("RequiresService", "Dapr")]
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

        // THE SIDECAR IS ACTUALLY THERE (#487). `DaprSidecarProbe` answers this
        // from the app's own side, so it is the same question Program.cs asks at
        // startup -- and it is false for an app started bare, which is how this
        // spec used to pass while proving nothing.
        var probe = await page.APIRequest.GetAsync("/api/health/dapr");
        Assert.True(probe.Ok, await probe.TextAsync());

        var status = await probe.TextAsync();
        Assert.Contains("\"available\":true", status, StringComparison.Ordinal);

        // And the page does not tell the user the sidecar is missing.
        await Assertions.Expect(page.GetByText("without a reachable Dapr sidecar"))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });

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
