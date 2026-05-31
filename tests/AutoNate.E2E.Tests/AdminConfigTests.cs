using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 7 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — admin
/// configuration. Covers the <c>ConfigLayout</c> shell behavior (the index
/// page, sidebar group expand/collapse, and a representative tour of the real
/// section routes), plus the <c>SiteSettingsForm</c> toolbar shape. IAM-side
/// CRUD (users, roles, groups, grants) lives in
/// <c>ManageUsersTests.cs</c> and <c>IamAdminTests.cs</c>.
/// </summary>
public sealed class AdminConfigTests : E2ETestBase
{
    public AdminConfigTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ConfigIndex_RendersSiteConfigurationHeading()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config");

        // sections.tsx:35 — <Title order={1}>Site Configuration</Title>.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Site Configuration" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task AdminConfig_SidebarGroup_ExpandsAndCollapses()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config");

        // ConfigLayout.tsx:58-59 — each top-level group is a <button> with
        // aria-expanded toggled on click. The accessible name is the group's
        // displayName (e.g. "Security", per the test-DB site-config menu seed).
        var securityGroup = page.GetByRole(AriaRole.Button, new() { Name = "Security" });
        await Assertions.Expect(securityGroup).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Initial state is collapsed (aria-expanded="false"); a click flips it.
        await Assertions.Expect(securityGroup).ToHaveAttributeAsync("aria-expanded", "false");
        await securityGroup.ClickAsync();
        await Assertions.Expect(securityGroup).ToHaveAttributeAsync("aria-expanded", "true");
        await securityGroup.ClickAsync();
        await Assertions.Expect(securityGroup).ToHaveAttributeAsync("aria-expanded", "false");
    }

    /// <summary>
    /// Walks a representative subset of the real (non-stub) config section
    /// routes and confirms each one mounts its expected h1 heading. The
    /// stubs (Manage Users/Groups/Roles/etc. under <c>/admin/config/...</c>)
    /// share the same `<Title order={1}>{stub.title}</Title>` shape but are
    /// not interesting to walk one-by-one — they're covered en masse by the
    /// route registration smoke (`AppShell` mounted + h1 visible).
    /// </summary>
    [Theory]
    [InlineData("/admin/config/general", "General")]
    [InlineData("/admin/config/features", "Features")]
    [InlineData("/admin/config/events", "Events")]
    [InlineData("/admin/config/system-health", "System Health")]
    [InlineData("/admin/config/pages-menus", "Pages / Menus")]
    public async Task AdminConfig_RealSections_MountWithExpectedHeading(string path, string heading)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync(path);

        // Exact=true so the Events page's per-transport h4s
        // (e.g. "Transport — site.events", "Transport — agent.events") don't
        // also bind to the h1 "Events" assertion via Playwright's default
        // case-normalized substring match. Every other heading in the theory
        // table is also a unique-exact h1.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task SiteSettingsForm_OnGeneralSection_RendersSaveAndResetToolbar()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/general");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "General" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // SiteSettingsForm.tsx 95-112 puts a Reset + Save changes button pair
        // in the PageHeader's actions slot. Both are disabled until the form
        // is dirty (`disabled={!dirty}`), but they're always rendered, so
        // role+name visibility proves the form mounted.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Reset" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task GeneralSettings_NotificationsHeaderToggle_PersistsAfterReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/general");
        var toggle = page.GetByLabel("Show notifications in header");
        await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var originalValue = await toggle.IsCheckedAsync();

        try
        {
            await toggle.ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status))
                .ToHaveTextAsync("Settings saved.");

            await page.ReloadAsync();
            toggle = page.GetByLabel("Show notifications in header");
            await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 15_000 });
            Assert.Equal(!originalValue, await toggle.IsCheckedAsync());
        }
        finally
        {
            await page.GotoAsync("/admin/config/general");
            toggle = page.GetByLabel("Show notifications in header");
            await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 15_000 });
            if (await toggle.IsCheckedAsync() != originalValue)
            {
                await toggle.ClickAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
                await Assertions.Expect(page.GetByRole(AriaRole.Status))
                    .ToHaveTextAsync("Settings saved.");
            }
        }
    }
}
