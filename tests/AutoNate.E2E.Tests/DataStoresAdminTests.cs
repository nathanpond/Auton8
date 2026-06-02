using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Smoke + create-modal coverage for the Phase 1 + Phase 2 pages of the
/// Data Stores &amp; Analytics Pipeline plan (<c>docs/plans/2026-05-30-data-stores-implementation.md</c>):
/// <c>/datastores</c>, <c>/dataconnectors</c>, and
/// <c>/datasets</c>. Each page gets a render test (h1 + "New"
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

        await page.GotoAsync("/datastores");

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
        await page.GotoAsync("/datastores");

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
        await page.GotoAsync("/datastores");

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

    [Fact]
    public async Task DataStores_FileStoreDetail_UploadAppearsInList()
    {
        // Proves the full create → detail → upload journey works through the
        // SPA. Phase 0's commit message claimed this but no UI shipped — the
        // DataStoreDetailPage that backs this test was the fix-list item #1
        // from the data-feature UI gap audit.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/datastores");

        // Create a fresh file-type store through the same modal flow the
        // other tests exercise — keeps the test independent of any seeded
        // state and gives us a unique store id to navigate into.
        var storeName = TestNames.Prefixed("ds");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(storeName);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The name cell is now a <Link>; clicking it navigates to
        // /datastores/{id}. DataStoreDetailPage renders the store name
        // inside a PageHeader h1 along with the kind badge.
        await page.GetByText(storeName).First.ClickAsync();
        await page.WaitForURLAsync("**/datastores/*", new() { Timeout = 15_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = storeName }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Open the upload modal. The dropzone wraps a hidden file input
        // we can drive directly via SetInputFilesAsync (same approach the
        // plugin-upload test uses); this bypasses the drag-drop event and
        // fires the Dropzone's onDrop with the provided payload.
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload file" }).ClickAsync();
        var uploadModal = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload file" });
        await Assertions.Expect(uploadModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var fileName = $"hello-{Guid.NewGuid():N}.txt";
        await uploadModal.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "text/plain",
            Buffer = System.Text.Encoding.UTF8.GetBytes("hello from the e2e suite\n")
        });
        await uploadModal.GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();
        await Assertions.Expect(uploadModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The file row appears in the folder listing. The page renders the
        // filename inside a <Text fw={500}> beside a folder icon — a text
        // match on the unique-per-test name is enough.
        await Assertions.Expect(page.GetByText(fileName).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task DataStores_FileStoreDetail_NewFolderAppearsInList()
    {
        // Companion to the upload test: proves the folder-CRUD wrappers
        // landed and the breadcrumb-driven navigation works end-to-end.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/datastores");

        var storeName = TestNames.Prefixed("ds");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(storeName);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByText(storeName).First.ClickAsync();
        await page.WaitForURLAsync("**/datastores/*", new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "New folder" }).ClickAsync();
        var folderModal = page.GetByRole(AriaRole.Dialog, new() { Name = "New folder" });
        await Assertions.Expect(folderModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var folderName = $"docs-{Guid.NewGuid():N}".Substring(0, 12);
        await folderModal.GetByLabel("Folder name").FillAsync(folderName);
        await folderModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(folderModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The new folder appears in the listing as an anchor with its name.
        await Assertions.Expect(page.GetByText(folderName).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ---- DataConnectors -------------------------------------------------

    [Fact]
    public async Task DataConnectors_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/dataconnectors");

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
        await page.GotoAsync("/dataconnectors");

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
        await page.GotoAsync("/dataconnectors");

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

        await page.GotoAsync("/datasets");

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
        await page.GotoAsync("/datasets");

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
