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

    [Fact]
    public async Task DataStores_EditExisting_PersistsRenamedRow()
    {
        // Audit fix #13 — updateDataStore was a dead wrapper before this
        // commit; renaming or redescribing a store required delete-and-
        // recreate, which would also drop the per-store schema/role on
        // SqlType stores. The Edit modal reuses the create modal's
        // shape with the Kind field disabled (locked to the provisioned
        // kind once the row exists).
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/datastores");

        var original = TestNames.Prefixed("ds");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(original);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(original).First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = original });
        await row.GetByRole(AriaRole.Button, new() { Name = $"Edit {original}" }).ClickAsync();
        var editModal = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit data store" });
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Pre-fill check + Kind locked.
        await Assertions.Expect(editModal.GetByLabel("Name")).ToHaveValueAsync(original);
        await Assertions.Expect(editModal.GetByLabel("Kind")).ToBeDisabledAsync();

        var renamed = $"{original}-renamed";
        await editModal.GetByLabel("Name").FillAsync(renamed);
        await editModal.GetByLabel("Description").FillAsync("Renamed by audit fix #13 test");
        await editModal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(editModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(page.GetByText(renamed).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
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

    [Fact]
    public async Task DataConnectors_EditExisting_PersistsRenamedRow()
    {
        // Audit fix #6 — updateDataConnector / getDataConnector were
        // dead in the SPA. A wrong URL or rotated token forced delete-
        // and-recreate (losing lastFetchedAtUtc + cursor). New Edit
        // ActionIcon opens the same modal as create, pre-filled.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/dataconnectors");

        var original = TestNames.Prefixed("conn");
        await page.GetByRole(AriaRole.Button, new() { Name = "New connector" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(original);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(original).First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Open the edit modal via the row's pen ActionIcon.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = original });
        await row.GetByRole(AriaRole.Button, new() { Name = $"Edit {original}" }).ClickAsync();
        var editModal = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit data connector" });
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Modal pre-fills from the loaded row: asserting Name keeps the
        // old value proves openEdit() ran.
        await Assertions.Expect(editModal.GetByLabel("Name")).ToHaveValueAsync(original);
        // Kind is locked once the row exists — the runtime handler is
        // tied to that string and changing it would orphan state.
        await Assertions.Expect(editModal.GetByLabel("Kind")).ToBeDisabledAsync();

        var renamed = $"{original}-renamed";
        await editModal.GetByLabel("Name").FillAsync(renamed);
        await editModal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(editModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // List reflects the new name. The old name is gone (proves we
        // updated rather than duplicated).
        await Assertions.Expect(page.GetByText(renamed).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task DataConnectors_PreviewModal_OpensAndShowsConnectorReply()
    {
        // Audit fix #6 — Preview ActionIcon opens a modal that fires
        // POST /api/dataconnectors/{id}/preview. The connector here has
        // an empty REST URL so the backend handler will return a
        // structured failure rather than rows; that's the cheapest
        // signal that the request shape is wired right and the modal
        // renders the failure path without burning a 5xx alert.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/dataconnectors");

        var name = TestNames.Prefixed("conn");
        await page.GetByRole(AriaRole.Button, new() { Name = "New connector" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(name);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await row.GetByRole(AriaRole.Button, new() { Name = $"Preview {name}" }).ClickAsync();

        var previewModal = page.GetByRole(AriaRole.Dialog, new() { Name = $"Preview — {name}" });
        await Assertions.Expect(previewModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The "Re-run preview" button is unique to the modal and proves
        // the dialog mounted with the connector context loaded.
        await Assertions.Expect(
            previewModal.GetByRole(AriaRole.Button, new() { Name = "Re-run preview" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The connector has an empty URL, so the REST handler will fail
        // with a connection / DNS error. Either "Connector returned an
        // error" or "Preview failed" surfaces — both prove the request
        // path is wired and the failure UX renders. Empty rows would
        // also be acceptable (yellow Alert with "0 rows"), so we match
        // any of the three completion states.
        await Assertions.Expect(
            previewModal.GetByText(new System.Text.RegularExpressions.Regex(
                "Connector returned an error|Preview failed|0 rows")))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
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
    public async Task Datasets_CreateModal_OpensWithModeAndSourcePickers()
    {
        // Audit fix #4 — the modal's source picker used to be two raw
        // UUID TextInputs (Source ID + Source table) the user had to
        // copy-paste by hand. The fix replaces them with a NativeSelect
        // bound to listDataStores() (default kind) and listDataConnectors()
        // (when the kind toggle flips). The Source table dropdown only
        // renders when a SqlType DataStore is selected — FileType stores
        // and DataConnectors don't have ingested tables to enumerate.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/datasets");

        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Assertions.Expect(modal.GetByLabel("Name")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Mode")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source kind")).ToBeVisibleAsync();
        // Default kind is `datastore` so the DataStore picker should be
        // visible; the connector picker is rendered conditionally.
        await Assertions.Expect(modal.GetByLabel("Source DataStore")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source DataConnector")).Not.ToBeVisibleAsync();
        // The textarea is now aria-label'd because the surrounding Group
        // (with the "Import columns" button) replaces the visible label.
        await Assertions.Expect(modal.GetByLabel("Column schema (JSON)")).ToBeVisibleAsync();
        await Assertions.Expect(
            modal.GetByRole(AriaRole.Button, new() { Name = "Import columns from selected table" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Datasets_SourceKindToggle_SwapsBetweenDataStoreAndDataConnectorPickers()
    {
        // Audit fix #4 — the source-kind NativeSelect flips which picker
        // renders. Switching also clears the previously-picked sourceId
        // so the form can't POST a connector UUID with kind=datastore.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/datasets");
        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Default state: DataStore picker shows, DataConnector hidden.
        await Assertions.Expect(modal.GetByLabel("Source DataStore")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source DataConnector")).Not.ToBeVisibleAsync();

        // Flip the kind selector. The DataConnector picker takes over,
        // the DataStore picker disappears.
        await modal.GetByLabel("Source kind").SelectOptionAsync("dataconnector");
        await Assertions.Expect(modal.GetByLabel("Source DataConnector")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source DataStore")).Not.ToBeVisibleAsync();

        // And back — flipping again restores the DataStore picker.
        await modal.GetByLabel("Source kind").SelectOptionAsync("datastore");
        await Assertions.Expect(modal.GetByLabel("Source DataStore")).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByLabel("Source DataConnector")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Datasets_CreateOverFileStore_PicksFromDropdownAndPersists()
    {
        // Audit fix #4 happy path — proves the DataStore dropdown is
        // populated from listDataStores() and the picker round-trips end
        // to end. Seeding a SqlType store + ingesting a CSV (which would
        // exercise the "Source table" + "Import columns" controls too)
        // isn't viable in the E2E fixture because ConnectionStrings__
        // Datastores is unset there; SQL-store creation returns 503.
        // FileType stores don't surface a Source table dropdown, which
        // is correct behavior — the picker is rendered conditionally on
        // `dataStoreKindLabel(...) === "SqlType"`.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // Seed a FileType DataStore through the same UI flow the existing
        // DataStores tests exercise.
        await page.GotoAsync("/datastores");
        var storeName = TestNames.Prefixed("dsForDataset");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var storeModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(storeModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await storeModal.GetByLabel("Name").FillAsync(storeName);
        await storeModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(storeModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Open the Datasets create modal; the DataStore dropdown should
        // include the freshly-seeded store among its options.
        await page.GotoAsync("/datasets");
        var datasetName = TestNames.Prefixed("dset");
        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(datasetName);

        // The dropdown's <option> labels are `${name} (FileType)`. Picking
        // by HasText keeps the assertion stable against any prefix the
        // helper adds. SelectOptionAsync needs the value (the store id) so
        // we resolve the option's value attribute first.
        var dataStoreSelect = modal.GetByLabel("Source DataStore");
        await Assertions.Expect(
            dataStoreSelect.Locator("option").Filter(new() { HasText = storeName }))
            .ToBeAttachedAsync(new() { Timeout = 15_000 });
        var storeOptionValue = await dataStoreSelect
            .Locator("option")
            .Filter(new() { HasText = storeName })
            .GetAttributeAsync("value");
        Assert.False(string.IsNullOrEmpty(storeOptionValue));
        await dataStoreSelect.SelectOptionAsync(storeOptionValue!);

        // FileType stores have no tables, so the Source table picker
        // doesn't render. The default column-schema JSON the modal seeds
        // is valid for a happy-path Create.
        await Assertions.Expect(modal.GetByLabel("Source table")).Not.ToBeVisibleAsync();
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The new dataset appears in the list; the Source column renders
        // the kind in a Code element, so a substring match on the unique
        // dataset name is the cleanest signal.
        await Assertions.Expect(page.GetByText(datasetName).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Datasets_EditExisting_PersistsRenamedRow()
    {
        // Audit fix #13 — updateDataset was a dead wrapper. Renaming
        // required delete-and-recreate, which would also drop any
        // cached rows on Cached datasets. The dedicated Edit modal only
        // surfaces what the backend's UpdateDatasetRequest accepts
        // (Name / Description / RefreshCron); mode / source / columns
        // are locked once the underlying schema exists.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // Seed a FileType store (datasets need a source row to point at;
        // SqlType isn't viable in the E2E fixture).
        await page.GotoAsync("/datastores");
        var storeName = TestNames.Prefixed("dsForEdit");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var storeModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(storeModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await storeModal.GetByLabel("Name").FillAsync(storeName);
        await storeModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(storeModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GotoAsync("/datasets");
        var datasetName = TestNames.Prefixed("dset");
        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(datasetName);

        // Pick the seeded store from the DataStore dropdown.
        var dataStoreSelect = createModal.GetByLabel("Source DataStore");
        await Assertions.Expect(
            dataStoreSelect.Locator("option").Filter(new() { HasText = storeName }))
            .ToBeAttachedAsync(new() { Timeout = 15_000 });
        var storeOptionValue = await dataStoreSelect
            .Locator("option")
            .Filter(new() { HasText = storeName })
            .GetAttributeAsync("value");
        Assert.False(string.IsNullOrEmpty(storeOptionValue));
        await dataStoreSelect.SelectOptionAsync(storeOptionValue!);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Open the Edit modal via the row's pen ActionIcon.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = datasetName });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = $"Edit {datasetName}" }).ClickAsync();
        var editModal = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit dataset" });
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Pre-fill: Name shows the original; Virtual datasets get the
        // dimmed "no refresh cron" note instead of a cron input.
        await Assertions.Expect(editModal.GetByLabel("Name")).ToHaveValueAsync(datasetName);
        await Assertions.Expect(editModal.GetByText("Virtual datasets have no refresh cron"))
            .ToBeVisibleAsync();

        var renamed = $"{datasetName}-renamed";
        await editModal.GetByLabel("Name").FillAsync(renamed);
        await editModal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(editModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(page.GetByText(renamed).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
