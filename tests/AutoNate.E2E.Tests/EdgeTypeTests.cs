using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 3 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — edge-type
/// (relationship-type) coverage: the legacy <c>/record-edge-types</c> URL
/// still redirects to <c>/record-relationship-types</c>, the inline create
/// modal mints a new type, and the Edges-tab "New link" dialog mounts
/// cleanly on a record's detail page. The full edge link/remove flow is a
/// later phase — Mantine's nested Selects + async target-record search aren't
/// worth the brittleness here when API-level edge enforcement is exhaustively
/// covered in <c>AutoNate.Web.Tests/Authorization/RecordEdgeEnforcementTests.cs</c>.
/// </summary>
public sealed class EdgeTypeTests : E2ETestBase
{
    public EdgeTypeTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task LegacyRecordEdgeTypes_RedirectsToRelationshipTypes_AndModalCreates()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // RedirectWithParams in appRoutes.tsx forwards the legacy URL.
        await page.GotoAsync("/record-edge-types");
        await page.WaitForURLAsync("**/record-relationship-types",
            new() { Timeout = 10_000 });

        // The inline create button is an ActionIcon with aria-label
        // "New relationship type".
        await page.GetByRole(AriaRole.Button, new() { Name = "New relationship type" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var shortCode = TestNames.ShortCode();
        var forwardName = TestNames.Prefixed("rel");

        // The dialog requires Short code + Forward name; cardinality defaults
        // to many_to_many and the inverse/directed switches are optional.
        await modal.GetByLabel("Short code").FillAsync(shortCode);
        await modal.GetByLabel("Forward name").FillAsync(forwardName);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(shortCode).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task RecordEdgesTab_NewLink_OpensDialog()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var type = await seeder.CreateRecordTypeAsync(TestNames.ShortCode(), TestNames.Prefixed("linkable"));
        var record = await seeder.CreateRecordAsync(type.Id, TestNames.Prefixed("rec"));

        await page.GotoAsync($"/record/{record.Key}");

        await page.GetByRole(AriaRole.Tab, new() { Name = "Edges" }).ClickAsync();

        // EdgesPanel disables the "New link" button until its useRecordType
        // resolves; ClickAsync waits for actionability so this races cleanly.
        await page.GetByRole(AriaRole.Button, new() { Name = "New link" }).ClickAsync();

        // EdgeLinkDialog mounts with title "Link to another record". Asserting
        // the dialog role + title is enough for the smoke — the dialog's
        // nested target-record Search/Select chain is deferred per the plan.
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(dialog.GetByText("Link to another record"))
            .ToBeVisibleAsync();
    }
}
