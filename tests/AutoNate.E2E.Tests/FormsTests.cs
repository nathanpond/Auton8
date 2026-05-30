using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 5 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — forms: the
/// `New form` modal on <c>/admin/config/forms</c> mints a form and lands on
/// the editor; the editor's `Publish` flashes a success alert and bumps the
/// `Published v#` chip; the published+site-available form renders for the
/// signed-in user at <c>/form/{shortCode}</c>; and dropping the
/// <c>siteAvailable</c> flag falls through to the "No published form
/// available" alert even after a publish.
///
/// The "submit succeeds" half of the original plan bullet (driving a real
/// form submission end-to-end) is deferred — the default form code
/// (<c>EfCoreFormStore.DefaultFormCode</c>) is a placeholder JSX that
/// intentionally has no inputs or submit button. Driving a real submit would
/// require authoring custom form JSX inside the editor, which is well outside
/// the cost/value of an E2E test (Form-store/snapshot authorization is
/// covered exhaustively in <c>AutoNate.Web.Tests/Authorization/FormEnforcementTests.cs</c>).
/// </summary>
public sealed class FormsTests : E2ETestBase
{
    public FormsTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task FormsList_CreateViaModal_NavigatesToEditor()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/forms");
        await page.GetByRole(AriaRole.Button, new() { Name = "New form" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var name = TestNames.Prefixed("form");
        var shortCode = $"e2e-{TestNames.ShortSlug()}"; // lowercase per modal styles

        await modal.GetByLabel("Name").FillAsync(name);
        await modal.GetByLabel("Short code").FillAsync(shortCode);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create & edit" }).ClickAsync();

        // After create the SPA navigates to /admin/config/forms/{id}; the
        // editor's PageHeader title is `form.name`, so a heading with the
        // freshly-typed name is the cheapest proof both the navigation and
        // the editor mount succeeded.
        await page.WaitForURLAsync("**/admin/config/forms/*", new() { Timeout = 10_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = name }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task FormEditor_Publish_ShowsPublishedFlash()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        // Seed the form via API rather than the create modal so the test
        // stays focused on the publish flow; the modal path is covered by
        // FormsList_CreateViaModal_NavigatesToEditor above.
        var form = await seeder.CreateFormAsync(
            name: TestNames.Prefixed("pubform"),
            shortCode: $"e2e-{TestNames.ShortSlug()}",
            siteAvailable: true);

        await page.GotoAsync($"/admin/config/forms/{form.Id}");

        // Wait for the editor to mount (the form name heading) so the
        // Publish button has its handler wired up.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = form.Name }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();

        // FormEditor.onPublish sets a green "Published." flash on success.
        await Assertions.Expect(page.GetByText("Published."))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // And the "Open live" toolbar button — only rendered when
        // `publishedVersionNumber !== null && siteAvailable` (FormEditor.tsx
        // lines 174-185) — should now appear. This is a more reliable
        // proof-of-publish signal than the · -prefixed `Published v1` chip,
        // whose accessible-name match races React Query's setQueryData
        // re-render against Playwright's text search.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Link, new() { Name = "Open live" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task PublishedSiteAvailableForm_RendersForSignedInUser()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var form = await seeder.CreateFormAsync(
            name: TestNames.Prefixed("liveform"),
            shortCode: $"e2e-{TestNames.ShortSlug()}",
            siteAvailable: true);
        await seeder.PublishFormAsync(form.Id);

        await page.GotoAsync($"/form/{form.ShortCode}");

        // EfCoreFormStore.DefaultFormCode renders an h3 "New form" inside the
        // JsxFormHost — that's the simplest signal the public snapshot
        // resolved and the JSX evaluator successfully rendered the form. The
        // yellow "No published form available" alert means the path failed
        // (either unpublished or site_available=false).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "New form" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText("No published form available"))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task PublishedButNotSiteAvailable_PublicViewShowsAlert()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        // siteAvailable=false: the form is created and even published, but the
        // backend's GET /api/forms/public/{shortCode} returns 404 (both
        // gates: published_version_number IS NOT NULL AND site_available).
        var form = await seeder.CreateFormAsync(
            name: TestNames.Prefixed("hiddenform"),
            shortCode: $"e2e-{TestNames.ShortSlug()}",
            siteAvailable: false);
        await seeder.PublishFormAsync(form.Id);

        await page.GotoAsync($"/form/{form.ShortCode}");

        // FormPublicView.tsx renders a yellow Alert reading
        // "No published form available at /form/{shortCode}." when the
        // snapshot is null.
        await Assertions.Expect(
            page.GetByText("No published form available"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
