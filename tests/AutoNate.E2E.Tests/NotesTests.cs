using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 6 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — Notes side.
/// Smoke-only per the plan's decision on heavy editors: <c>/notes</c> mounts
/// the explorer shell without crashing, and the all-projects DataTable at
/// <c>/projects</c> renders with its `Add project` affordance. We deliberately
/// don't drive BlockNote typing or the cabinet/notebook/page modal chain —
/// they're Yjs- and contenteditable-heavy, and the plan keeps E2E coverage
/// behavioural on the surrounding shell rather than on the editor internals.
/// </summary>
public sealed class NotesTests : E2ETestBase
{
    public NotesTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task NotesPage_MountsExplorerShellWithoutError()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/notes");

        // NotesPage doesn't use PageHeader (full-bleed layout) and the
        // ProjectSelector's "Search projects…" placeholder is gated on a
        // dropdown being open. The reliable always-on signal is the
        // CabinetRail's `New cabinet` (or `Select a project to add cabinets`)
        // ActionIcon — it mounts unconditionally in the sidebar.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button,
                new() { NameRegex = new System.Text.RegularExpressions.Regex("New cabinet|Select a project") }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // No red Alert means the project list query + Yjs-list bootstrap
        // didn't blow up.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AllProjectsPage_RendersHeadingAndAddProject()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/projects");

        // PageHeader title="All projects" (AllProjects.tsx:104) → h1.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "All projects" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The `Add project` button has an explicit aria-label
        // (AllProjects.tsx:140) — any signed-in user can create projects, so
        // admin always sees it.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Add project" }))
            .ToBeVisibleAsync();
    }
}
