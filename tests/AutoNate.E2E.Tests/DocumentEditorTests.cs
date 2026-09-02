using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class DocumentEditorTests : E2ETestBase
{
    public DocumentEditorTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DocumentEditor_ContentPersistsAcrossReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("doc-content-proj"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("doc-content"));
        var bodyText = TestNames.Prefixed("persisted-body");

        await page.GotoAsync($"/documents/edit/{document.Id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Back to project" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        var editor = page.Locator("[contenteditable='true']").First;
        await Assertions.Expect(editor).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await editor.EvaluateAsync("element => element.focus()");
        await page.Keyboard.InsertTextAsync(bodyText);
        await Assertions.Expect(editor).ToContainTextAsync(bodyText);

        // This used to be a fixed 3 s sleep standing in for "Hocuspocus has
        // debounced and persisted the Y.Doc", which passed on a fast laptop and
        // failed on a loaded runner — where it read as a product bug (archived-89).
        //
        // There is no server-side signal to poll for a *document* body:
        // persistence goes sidecar → yjs_documents with no API over it, and the
        // content-version bump the webhook does is page-specific, so the
        // version stays put here (measured). So retry the real assertion
        // instead of guessing at a duration before it: reload, look for the
        // text, and if it is not there yet reload again. Disconnecting on
        // reload is itself what flushes the sidecar, so a too-early attempt
        // costs one extra round trip rather than losing the edit.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var seen = false;
        while (DateTime.UtcNow < deadline)
        {
            await page.ReloadAsync();
            var reloaded = page.Locator("[contenteditable='true']").First;
            try
            {
                await Assertions.Expect(reloaded).ToContainTextAsync(bodyText, new() { Timeout = 10_000 });
                seen = true;
                break;
            }
            catch (PlaywrightException)
            {
                // Not persisted yet — go round again until the deadline.
            }
        }

        Assert.True(seen, $"Document body '{bodyText}' did not survive a reload within 60s.");
    }

    [Fact]
    public async Task ProjectDocuments_ImportDocx_CommitsImportedContent()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("doc-import-proj"));
        const string importedText = "AutoNate imported fixture text";
        var fixturePath = CreateSerializedDocx();

        try
        {
            await page.GotoAsync($"/documents/p/{project.Id}");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Project root" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });

            var uploadResponse = await page.RunAndWaitForResponseAsync(
                () => page.Locator("input[type='file'][accept='.docx']").SetInputFilesAsync(fixturePath),
                response => response.Url.Contains("/api/content/documents/import", StringComparison.Ordinal));
            Assert.True(uploadResponse.Ok, await uploadResponse.TextAsync());
            await Assertions.Expect(page).ToHaveURLAsync(
                new Regex(@"/documents/edit/[0-9a-f-]+(?:\?import=1)?$"),
                new() { Timeout = 30_000 });
            await Assertions.Expect(page).ToHaveURLAsync(
                new Regex(@"/documents/edit/[0-9a-f-]+$"),
                new() { Timeout = 30_000 });
            // The imported text has to have reached the document's stored
            // body, which is what "finalize" means: the parsed ProseMirror
            // state is PATCHed into body_jsonb and the stash is discarded.
            //
            // Asserted through the API rather than the editor surface on
            // purpose. Once import mode ends the editor renders through the
            // Yjs sidecar, and this fixture does not run one — the browser
            // logs `authentication-failed` and paints an empty body no
            // matter what was saved. body_jsonb is the artifact this fix is
            // responsible for; the sidecar's cold-load seed reads it.
            var stored = await page.APIRequest.GetAsync(
                $"/api/content/documents/{DocumentIdFromUrl(page.Url)}");
            Assert.True(stored.Ok, await stored.TextAsync());
            var storedJson = await stored.TextAsync();
            Assert.Contains(importedText, storedJson, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Fact]
    public async Task DocumentEditor_PreviewsAndDownloadsDocx()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("doc-preview-proj"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("doc-preview"));

        await page.GotoAsync($"/documents/edit/{document.Id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Back to project" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        var download = await page.RunAndWaitForDownloadAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Download .docx" }).ClickAsync());
        Assert.Equal($"{document.Title}.docx", download.SuggestedFilename);

        await page.GetByRole(AriaRole.Link, new() { Name = "Preview" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(
            new Regex($@"/documents/preview/{document.Id}$"),
            new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText("Preview", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Back to project" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DocumentEditor_OpensHistoricalVersionPreview()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("doc-version-proj"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("doc-version"));

        await page.GotoAsync($"/documents/edit/{document.Id}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle version history" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Version history", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var versionPageTask = session.Context.WaitForPageAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "View version 1" }).ClickAsync();
        var versionPage = await versionPageTask;
        await Assertions.Expect(versionPage).ToHaveURLAsync(
            new Regex($@"/documents/preview/{document.Id}\?version=1$"),
            new() { Timeout = 15_000 });
        await Assertions.Expect(versionPage.GetByText("Version 1", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(versionPage.GetByRole(AriaRole.Link, new() { Name = "Edit" }))
            .ToBeVisibleAsync();
        await versionPage.CloseAsync();
    }

    [Fact]
    public async Task DocumentEditor_InsertsRecordFieldAndAqlBindings()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("doc-binding-proj"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("doc-binding"));
        var recordType = await seeder.CreateRecordTypeAsync(
            TestNames.ShortCode(),
            TestNames.Prefixed("binding-type"));
        var record = await seeder.CreateRecordAsync(recordType.Id, TestNames.Prefixed("binding-record"));
        var fieldLabel = TestNames.Prefixed("record-binding");
        var tableLabel = TestNames.Prefixed("table-binding");

        await page.GotoAsync($"/documents/edit/{document.Id}");
        await Assertions.Expect(page.GetByText("Live data bindings", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Insert binding" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Insert live data binding" });
        await dialog.GetByLabel("Label (optional)").FillAsync(fieldLabel);
        await dialog.GetByLabel("Record ID").FillAsync(record.Id.ToString());
        await dialog.GetByLabel("Field key").FillAsync("name");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Insert", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button).Filter(new() { HasText = fieldLabel }))
            .ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Insert binding" }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Insert live data binding" });
        await dialog.GetByRole(AriaRole.Combobox, new() { Name = "Binding kind" }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "AQL table — run a query, render results as a table" })
            .ClickAsync();
        await dialog.GetByLabel("Label (optional)").FillAsync(tableLabel);
        await dialog.GetByLabel("AQL query").FillAsync("FROM Records");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Insert", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button).Filter(new() { HasText = tableLabel }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Documents_GrantAndRevokeDocumentAndFolderOverrides()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("doc-permission-proj"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("doc-permission"));
        var username = TestNames.Prefixed("shared-user");
        await seeder.CreateUserAsync(username, "Password123!");

        await page.GotoAsync($"/documents/edit/{document.Id}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Permissions" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Permissions — {document.Title}" });
        await GrantAndRevokeViewOverrideAsync(page, dialog, username);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();

        await page.GotoAsync($"/documents/p/{project.Id}");
        var folderName = TestNames.Prefixed("shared-folder");
        await page.GetByRole(AriaRole.Button, new() { Name = "New folder", Exact = true }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.GetByLabel("Name").FillAsync(folderName);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(page.GetByText(folderName, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Folder menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Permissions" }).ClickAsync();
        dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Permissions — {folderName}" });
        await GrantAndRevokeViewOverrideAsync(page, dialog, username);
    }

    private static async Task GrantAndRevokeViewOverrideAsync(IPage page, ILocator dialog, string username)
    {
        await dialog.GetByRole(AriaRole.Combobox, new() { Name = "User" }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = username, Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Combobox, new() { Name = "Action" }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "View", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Grant" }).ClickAsync();
        await Assertions.Expect(dialog.GetByText(username, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Revoke override" }).ClickAsync();
        await Assertions.Expect(dialog.GetByText(username, new() { Exact = true }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private static Guid DocumentIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"/documents/edit/([0-9a-f-]+)");
        Assert.True(match.Success, $"Could not read a document id out of '{url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }

    private static string CreateSerializedDocx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        File.WriteAllBytes(path, Convert.FromBase64String(SerializedDocxBase64));
        return path;
    }

    private const string SerializedDocxBase64 = "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";

}
