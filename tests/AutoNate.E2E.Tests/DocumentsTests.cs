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
}
