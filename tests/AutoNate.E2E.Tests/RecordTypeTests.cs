using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 3 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — record-type
/// CRUD that sits one layer above records: creating a type from the inline
/// modal on the list, defining a field on the editor and confirming
/// persistence, and round-tripping archive → restore on the editor's badge.
///
/// Edge-types live in <c>EdgeTypeTests.cs</c> alongside the legacy-redirect
/// check and the Edges-tab smoke test.
/// </summary>
public sealed class RecordTypeTests : E2ETestBase
{
    public RecordTypeTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task RecordTypeList_CreateViaModal_AddsRowToTable()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/record-types");
        await page.GetByRole(AriaRole.Button, new() { Name = "New record type" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var shortCode = TestNames.ShortCode();
        var typeName = TestNames.Prefixed("inline");

        // Mantine's NewRecordType modal: Short code, Name, optional desc.
        // Submit is the "Create" button (the Cancel button is variant="default").
        await modal.GetByLabel("Short code").FillAsync(shortCode);
        await modal.GetByLabel("Name").FillAsync(typeName);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // Modal closes and the DataTable refetches — the new short code shows
        // up as a row. We assert on the short code because the name column
        // also has a search input with the same placeholder, but the code is
        // unique per test.
        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(shortCode).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task RecordTypeEditor_AddField_PersistsAcrossReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var type = await seeder.CreateRecordTypeAsync(TestNames.ShortCode(), TestNames.Prefixed("schema"));

        await page.GotoAsync($"/record-types/{type.Id}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add field" }).ClickAsync();

        // The Add field modal uses TextInputs for "Field key" (lowercase
        // snake_case) and "Display name", plus a NativeSelect "Data type".
        // The default data type (first option) is fine for a smoke field.
        var fieldModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(fieldModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var fieldKey = $"e2e_{TestNames.ShortSlug()}";
        var displayName = TestNames.Prefixed("field");

        await fieldModal.GetByLabel("Field key").FillAsync(fieldKey);
        await fieldModal.GetByLabel("Display name").FillAsync(displayName);
        await fieldModal.GetByRole(AriaRole.Button, new() { Name = "Add field" }).ClickAsync();

        await Assertions.Expect(fieldModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Field is listed by key in the Fields table.
        await Assertions.Expect(page.GetByText(fieldKey).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Reload to prove server-side persistence rather than just optimistic
        // cache.
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText(fieldKey).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task RecordTypeEditor_ArchiveAndRestore_FlipsBadge()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var type = await seeder.CreateRecordTypeAsync(TestNames.ShortCode(), TestNames.Prefixed("life"));

        await page.GotoAsync($"/record-types/{type.Id}");

        // Fresh type → not archived, so the toggle button reads "Archive".
        await Assertions.Expect(page.GetByText("Archived", new() { Exact = true }).First).Not.ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();

        // After archive, the header renders a gray "Archived" badge and the
        // toggle flips to "Restore".
        await Assertions.Expect(page.GetByText("Archived", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Restore" }).ClickAsync();

        // After restore the badge disappears and the toggle is back to
        // "Archive".
        await Assertions.Expect(page.GetByText("Archived", new() { Exact = true }).First).Not.ToBeVisibleAsync(
            new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Archive" }))
            .ToBeVisibleAsync();
    }
}
