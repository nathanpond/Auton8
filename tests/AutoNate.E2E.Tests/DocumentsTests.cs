using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 6 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — Documents side.
/// Drives the project picker, folder-tree view + Mantine create-folder /
/// create-document modals (these are plain Mantine modals so they're safe to
/// drive), the document-editor route mount, and the cross-project template
/// gallery. The docx editor's ProseMirror surface is asserted only at the
/// mount level — full editor automation is deliberately out of scope per the
/// plan's smoke+API-backed decision (Yjs + tracked changes + comments + the
/// agent panel are well beyond what's worth driving from .NET Playwright).
/// </summary>
public sealed class DocumentsTests : E2ETestBase
{
    public DocumentsTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DocumentsHome_RendersHeadingAndTemplateGalleryLink()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/documents");

        // DocumentsHomePage's PageHeader: title="Documents".
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Documents" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The Template gallery button is a Link styled as Button (line 99 of
        // DocumentsHomePage.tsx).
        await Assertions.Expect(
            page.GetByRole(AriaRole.Link, new() { Name = "Template gallery" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task ProjectDocuments_CreateFolderAndDocument_ViaModals()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("docproj"));

        await page.GotoAsync($"/documents/p/{project.Id}");

        // The empty project root renders `Title order={3}` "Project root"
        // (ProjectDocumentsPage.tsx:164). Wait for it so the toolbar buttons
        // (which mount in the same render) are present.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Project root" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // --- New folder ---
        // The Mantine sidebar tree also renders an ActionIcon with
        // aria-label="New folder at root", so we need Exact=true to bind to
        // the toolbar `<Button>` with text "New folder".
        var folderName = TestNames.Prefixed("folder");
        await page.GetByRole(AriaRole.Button, new() { Name = "New folder", Exact = true }).ClickAsync();
        var folderModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(folderModal).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await folderModal.GetByLabel("Name").FillAsync(folderName);
        await folderModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(folderModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(folderName).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // --- New document ---
        var documentTitle = TestNames.Prefixed("doc");
        await page.GetByRole(AriaRole.Button, new() { Name = "New document" }).ClickAsync();
        var docModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(docModal).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await docModal.GetByLabel("Title").FillAsync(documentTitle);
        await docModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(docModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(documentTitle).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task DocumentEditor_PageMountsForSeededDocument()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("editorproj"));
        var document = await seeder.CreateDocumentAsync(project.Id, TestNames.Prefixed("editordoc"));

        await page.GotoAsync($"/documents/edit/{document.Id}");

        // The editor route is full-bleed (no AppShell) and lazy-loads
        // @eigenpal/docx-editor-react. Mount signal: the back-to-project
        // affordance (top of DocumentEditorPage) — present regardless of
        // editor-internal state. We give it more headroom than usual because
        // the lazy chunk + ProseMirror init is one of the slower lazy
        // routes in the SPA.
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Back to project" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        // No red Alert: docx-editor-react didn't crash on import.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task TemplateGallery_RendersHeading()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/documents/templates");

        // TemplateGalleryPage's PageHeader: title="Template Gallery".
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Template Gallery" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task TemplateGallery_CreateTemplate_HidesFromProjectView_AndDeletes()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("templateproj"));
        var templateTitle = TestNames.Prefixed("template");

        await page.GotoAsync("/documents/templates");
        await page.GetByRole(AriaRole.Button, new() { Name = "New template" }).ClickAsync();
        var createDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "New template" });
        await createDialog.GetByRole(AriaRole.Combobox, new() { Name = "Project" }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = project.Name, Exact = true }).ClickAsync();
        await createDialog.GetByLabel("Template title").FillAsync(templateTitle);

        var editorPageTask = session.Context.WaitForPageAsync();
        await createDialog.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        var editorPage = await editorPageTask;
        await editorPage.CloseAsync();
        await Assertions.Expect(page.GetByText(templateTitle, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GotoAsync($"/documents/p/{project.Id}");
        await Assertions.Expect(page.GetByText(templateTitle, new() { Exact = true }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GotoAsync("/documents/templates");
        await Assertions.Expect(page.GetByText(templateTitle, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Template actions" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete" }).ClickAsync();
        var deleteDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Delete template" });
        await deleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await Assertions.Expect(deleteDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(templateTitle, new() { Exact = true }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task ProjectDocuments_FolderDeepLink_RestoresAfterReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("deeplinkproj"));
        var folderName = TestNames.Prefixed("folder");

        await page.GotoAsync($"/documents/p/{project.Id}");
        await CreateFolderAsync(page, folderName);
        await page.GetByText(folderName, new() { Exact = true }).Last.ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = folderName }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var folderUrl = page.Url;
        await page.ReloadAsync();

        Assert.Equal(folderUrl, page.Url);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = folderName }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "New subfolder" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task ProjectDocuments_RenameAndDeleteDocument_ViaCardMenu()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("docmutationproj"));
        var originalTitle = TestNames.Prefixed("doc");
        var renamedTitle = TestNames.Prefixed("doc-renamed");
        await seeder.CreateDocumentAsync(project.Id, originalTitle);

        await page.GotoAsync($"/documents/p/{project.Id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = originalTitle }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Document menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Rename" }).ClickAsync();
        var renameDialog = page.GetByRole(AriaRole.Dialog);
        await renameDialog.GetByLabel("Title").FillAsync(renamedTitle);
        await renameDialog.GetByRole(AriaRole.Button, new() { Name = "Rename" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = renamedTitle }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Document menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete document" }).ClickAsync();
        var deleteDialog = page.GetByRole(AriaRole.Dialog);
        await deleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = renamedTitle }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task ProjectDocuments_RenameAndDeleteFolder_ViaTreeMenu()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("foldermutationproj"));
        var originalName = TestNames.Prefixed("folder");
        var renamedName = TestNames.Prefixed("folder-renamed");

        await page.GotoAsync($"/documents/p/{project.Id}");
        await CreateFolderAsync(page, originalName);

        await page.GetByRole(AriaRole.Button, new() { Name = "Folder menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Rename" }).ClickAsync();
        var renameDialog = page.GetByRole(AriaRole.Dialog);
        await renameDialog.GetByLabel("Name").FillAsync(renamedName);
        await renameDialog.GetByRole(AriaRole.Button, new() { Name = "Rename" }).ClickAsync();
        await Assertions.Expect(page.GetByText(renamedName, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Folder menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete folder" }).ClickAsync();
        var deleteDialog = page.GetByRole(AriaRole.Dialog);
        await deleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Assertions.Expect(deleteDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        // Count, not visibility. The folder name renders in more than one
        // place while the delete is in flight (the tree node and the header
        // above it), and Not.ToBeVisibleAsync on a locator that matches two
        // elements is itself a strict-mode violation — the assertion fails on
        // the ambiguity rather than on the folder still being there. Asking
        // for zero matches says what this means and is unambiguous however
        // many places the name appears.
        await Assertions.Expect(page.GetByText(renamedName, new() { Exact = true }))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    private static async Task CreateFolderAsync(IPage page, string folderName)
    {
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Project root" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "New folder", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.GetByLabel("Name").FillAsync(folderName);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByText(folderName, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
