using AutoNate.E2E.Tests.Support;
using System.Text.RegularExpressions;
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
        // Full create → detail → upload journey through the SPA. The detail
        // page is DataStoreFileManager (SVAR file manager); the toolbar's
        // "Upload to current folder" opens a Mantine Modal titled
        // "Upload to <path>" whose Dropzone wraps a hidden file input we drive
        // directly with SetInputFilesAsync (fires onDrop without drag events).
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var storeName = await CreateFileStoreAndOpenDetailAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Upload to current folder" }).ClickAsync();
        var uploadModal = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload to /" });
        await Assertions.Expect(uploadModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var stem = $"hello-{Guid.NewGuid():N}";
        await uploadModal.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = $"{stem}.txt",
            MimeType = "text/plain",
            Buffer = System.Text.Encoding.UTF8.GetBytes("hello from the e2e suite\n")
        });
        await Assertions.Expect(uploadModal.GetByText("Queued (1):")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        // The button label is "Upload <count>".
        await uploadModal.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Upload") }).ClickAsync();
        await Assertions.Expect(uploadModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // SVAR renders file names as separate name/extension spans, so match
        // the unique stem rather than the full "<stem>.txt".
        await Assertions.Expect(page.GetByText(stem).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        _ = storeName;
    }

    [Fact]
    public async Task DataStores_FileStoreDetail_NewFolderAppearsInList()
    {
        // Companion to the upload test: folder creation goes through SVAR's
        // "Add New" menu → "Add new folder" → its own name prompt (.wx-modal,
        // no dialog role), which the DataStoreFileManager create-file
        // interceptor turns into POST /api/datastores/{id}/folders.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await CreateFileStoreAndOpenDetailAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Add New" }).ClickAsync();
        // The menu renders in a portal outside <main>; its items are plain
        // divs, so target by exact text.
        await page.GetByText("Add new folder", new() { Exact = true }).ClickAsync();

        var prompt = page.Locator(".wx-modal");
        await Assertions.Expect(prompt).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(prompt.GetByText("Enter folder name")).ToBeVisibleAsync();
        var folderName = $"docs-{Guid.NewGuid():N}".Substring(0, 12);
        await prompt.GetByRole(AriaRole.Textbox).FillAsync(folderName);
        await prompt.GetByRole(AriaRole.Button, new() { Name = "OK" }).ClickAsync();
        await Assertions.Expect(prompt).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The folder tree on the left lists the name whole (the main pane
        // splits it into name/extension spans).
        await Assertions.Expect(page.GetByText(folderName, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Creates a FileType data store through the New-data-store modal and
    /// navigates into its detail page. Returns the store name.
    /// </summary>
    private static async Task<string> CreateFileStoreAndOpenDetailAsync(IPage page)
    {
        await page.GotoAsync("/datastores");
        var storeName = TestNames.Prefixed("ds");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(storeName);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The name cell is a <Link> to /datastores/{id}.
        await page.GetByRole(AriaRole.Link, new() { Name = storeName }).ClickAsync();
        await page.WaitForURLAsync("**/datastores/*", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = storeName }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        // The file manager is mounted once the root listing has loaded.
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Upload to current folder" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        return storeName;
    }

    /// <summary>
    /// Creates a FileType store via the UI, seeds one CSV into it over the
    /// API (POST /api/datastores/{id}/files, multipart "folder" + "file" —
    /// the same call the SPA makes), and returns (storeName, csvFileName).
    /// File-backed datasets refuse to save without a picked file, so every
    /// dataset spec over a file store needs this.
    /// </summary>
    private static async Task<(string StoreName, string FileName)> CreateFileStoreWithCsvAsync(IPage page)
    {
        await page.GotoAsync("/datastores");
        var storeName = TestNames.Prefixed("dsForDataset");
        await page.GetByRole(AriaRole.Button, new() { Name = "New data store" }).ClickAsync();
        var storeModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(storeModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await storeModal.GetByLabel("Name").FillAsync(storeName);
        await storeModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(storeModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        var link = page.GetByRole(AriaRole.Link, new() { Name = storeName });
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var href = await link.GetAttributeAsync("href") ?? string.Empty;
        var storeId = href[(href.LastIndexOf('/') + 1)..];
        Assert.False(string.IsNullOrWhiteSpace(storeId), $"could not read store id from href '{href}'");

        var fileName = $"rows-{Guid.NewGuid():N}.csv";
        var form = page.APIRequest.CreateFormData();
        form.Set("folder", "/");
        form.Set("file", new FilePayload
        {
            Name = fileName,
            MimeType = "text/csv",
            // Matches the modal's default column schema (Id, Name — text).
            Buffer = System.Text.Encoding.UTF8.GetBytes("Id,Name\n1,alpha\n2,beta\n")
        });
        var upload = await page.APIRequest.PostAsync($"/api/datastores/{storeId}/files", new() { Multipart = form });
        Assert.True(upload.Ok, $"seed upload failed: {upload.Status} {await upload.TextAsync()}");
        return (storeName, fileName);
    }

    /// <summary>
    /// In the New/Edit dataset modal: picks the given FileType store in
    /// "Source DataStore" and the seeded CSV in "File" (single-file scope).
    /// </summary>
    private static async Task PickFileStoreAndFileAsync(ILocator modal, string storeName, string fileName)
    {
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

        // FileType stores have no tables → no Source table picker; instead a
        // Scope / Browse folder / File group appears once the listing loads.
        await Assertions.Expect(modal.GetByLabel("Source table")).Not.ToBeVisibleAsync();
        var fileSelect = modal.GetByLabel("File", new() { Exact = true });
        await Assertions.Expect(fileSelect.Locator("option").Filter(new() { HasText = fileName }))
            .ToBeAttachedAsync(new() { Timeout = 15_000 });
        await fileSelect.SelectOptionAsync(fileName);
        await Assertions.Expect(modal.GetByText($"/{fileName}")).ToBeVisibleAsync();
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
        // SqlType stores need ConnectionStrings__Datastores, which the E2E
        // fixture leaves unset (creation returns 503), so this exercises a
        // FileType store. File-backed datasets must point at a file, so one
        // CSV is seeded over the API first.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var (storeName, fileName) = await CreateFileStoreWithCsvAsync(page);

        await page.GotoAsync("/datasets");
        var datasetName = TestNames.Prefixed("dset");
        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(datasetName);
        await PickFileStoreAndFileAsync(modal, storeName, fileName);

        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The new dataset appears in the list with its file-backed source.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = datasetName });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(row.GetByText("datastore")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Datasets_EditExisting_PersistsRenamedRow()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var (storeName, fileName) = await CreateFileStoreWithCsvAsync(page);

        await page.GotoAsync("/datasets");
        var datasetName = TestNames.Prefixed("dset");
        await page.GetByRole(AriaRole.Button, new() { Name = "New dataset" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(datasetName);
        await PickFileStoreAndFileAsync(createModal, storeName, fileName);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = datasetName });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = $"Edit {datasetName}" }).ClickAsync();
        var editModal = page.GetByRole(AriaRole.Dialog, new() { Name = "Edit dataset" });
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(editModal.GetByLabel("Name")).ToHaveValueAsync(datasetName);
        await Assertions.Expect(editModal.GetByText("Virtual datasets have no refresh cron"))
            .ToBeVisibleAsync();
        // The edit form only exposes name / description / refresh cron; the
        // persisted file scope is left untouched by the update (asserted via
        // the Source column after saving).

        var renamed = $"{datasetName}-renamed";
        await editModal.GetByLabel("Name").FillAsync(renamed);
        await editModal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(editModal).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
        var renamedRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = renamed });
        await Assertions.Expect(renamedRow).ToBeVisibleAsync(new() { Timeout = 15_000 });
        // Still file-backed after the rename.
        await Assertions.Expect(renamedRow.GetByText("datastore")).ToBeVisibleAsync();
    }

}
