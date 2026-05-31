using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Smoke + create-modal coverage for the Phase 1 + Phase 2 admin pages of the
/// Data Stores &amp; Analytics Pipeline plan (<c>docs/plans/2026-05-30-data-stores-implementation.md</c>):
/// <c>/admin/config/datastores</c>, <c>/admin/config/dataconnectors</c>, and
/// <c>/admin/config/datasets</c>. Each page gets a render test (h1 + "New"
/// button visible) plus a modal-opens-with-fields test. The datastores +
/// dataconnectors pages also have a create-then-list happy path; the
/// datasets page's create form takes a Source ID that has to point at a
/// real DataStore UUID, so its happy path needs a precondition the smoke
/// tests already cover via the file-type datastore created in the
/// datastores happy path. We keep the smaller per-page test in this file
/// rather than splitting because every assertion is one HTTP round trip.
/// </summary>
public sealed class DataStoresAdminTests : E2ETestBase
{
    public DataStoresAdminTests(AutoNateE2EFixture fixture) : base(fixture) { }

    // ---- DataStores -----------------------------------------------------

    [Fact]
    public async Task DataStores_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/datastores");

        // DataStoresPage.tsx — Title order={1} "Data Stores".
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Data Stores", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The toolbar-right button always renders for the seeded admin
        // (kind-level Create grant present via SuperAdmin backfill).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "New data store" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DataStores_CreateModal_OpensWithRequiredFields()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/datastores");

        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Name + Kind are the load-bearing fields on the create form
        // (description is optional). The Kind NativeSelect has aria-label
        // "Kind" supplied by Mantine from the label prop.
        await Assertions.Expect(modal.GetByLabel("Name")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Kind")).ToBeVisibleAsync();
        await Assertions.Expect(
            modal.GetByRole(AriaRole.Button, new() { Name = "Create" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DataStores_CreateFileStore_AppearsInList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/datastores");

        var name = TestNames.Prefixed("ds");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(name);
        // FileType is the default; no need to interact with the Kind dropdown.
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // Mantine notifications show "Data store created." on success and
        // the modal closes. The new row appears in the DataTable below.
        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Kind badge for a File-type store: the DataTable renders the kind
        // label (kindLabel(row.kind) === "FileType"). Asserting on the
        // unique-per-test name above is enough; the badge is incidental.
    }

    // ---- DataConnectors -------------------------------------------------

    [Fact]
    public async Task DataConnectors_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/dataconnectors");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Data Connectors", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "New connector" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DataConnectors_CreateModal_OpensWithKindAndConfigFields()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/dataconnectors");

        await page.GetByRole(AriaRole.Button, new() { Name = "New connector" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Kind is populated from GET /api/dataconnectors/kinds — the
        // dropdown defaults to "rest". Config JSON is a Textarea that gets
        // a kind-specific JSON skeleton seeded.
        await Assertions.Expect(modal.GetByLabel("Name")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Kind")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Config JSON")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DataConnectors_CreateRestConnector_AppearsInList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/dataconnectors");

        var name = TestNames.Prefixed("conn");
        await page.GetByRole(AriaRole.Button, new() { Name = "New connector" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(name);
        // The page seeds {"url":"","authMode":"none"} into the Config
        // textarea on "rest" — that's valid JSON, so we can submit
        // straight away. The host accepts an empty URL at create time;
        // only the test-connection action would surface that defect.
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ---- Datasets -------------------------------------------------------

    [Fact]
    public async Task Datasets_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/datasets");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Datasets", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Datasets_CreateModal_OpensWithModeAndSourceFields()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/admin/config/datasets");

        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Datasets are richer to author — the create modal has Mode,
        // Source kind, Source ID, and a JSON column-schema textarea. We
        // smoke the load-bearing controls here; the happy path (which
        // requires a real source-id) is left to a follow-up commit when
        // ApiSeeder grows a CreateDataStoreAsync helper.
        await Assertions.Expect(modal.GetByLabel("Name")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Mode")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source kind")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source ID")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Column schema (JSON)")).ToBeVisibleAsync();
    }
}
