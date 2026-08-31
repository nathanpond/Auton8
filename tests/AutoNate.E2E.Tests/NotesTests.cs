using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 6 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — Notes side.
/// The suite keeps the Yjs-backed rich-text editor smoke-only, but drives the
/// surrounding content hierarchy through its lightweight modals: project,
/// cabinet, notebook, and page creation.
/// </summary>
public sealed class NotesTests : E2ETestBase
{
    public NotesTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task NotesPage_MountsExplorerShellWithoutError()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/notes");

        // NotesPage doesn't use PageHeader (full-bleed layout) and the
        // ProjectSelector's "Search projects…" placeholder is gated on a
        // dropdown being open. The reliable always-on signal is the
        // CabinetRail's `New cabinet` (or `Select a project to add cabinets`)
        // ActionIcon — it mounts unconditionally in the sidebar.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button,
                new() { NameRegex = new System.Text.RegularExpressions.Regex("New cabinet|Select a project") }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // No red Alert means the project list query + Yjs-list bootstrap
        // didn't blow up.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AllProjectsPage_RendersHeadingAndAddProject()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/projects");

        // PageHeader title="All projects" (AllProjects.tsx:104) → h1.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "All projects" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The `Add project` button has an explicit aria-label
        // (AllProjects.tsx:140) — any signed-in user can create projects, so
        // admin always sees it.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Add project" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task NotesHierarchy_CreateProjectCabinetNotebookAndPage_ViaUi()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var hierarchy = await CreateHierarchyAsync(page);

        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/notes/\d+$"));
        await Assertions.Expect(page.GetByText(hierarchy.PageTitle, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task NotesSidebar_CollapsePersistsAcrossReload_AndCanBeRestored()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/notes");
        await page.GetByRole(AriaRole.Button, new() { Name = "Collapse sidebar" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Show sidebar" }))
            .ToBeVisibleAsync();

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Show sidebar" }))
            .ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Show sidebar" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Collapse sidebar" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task NotesExplorer_SearchFiltersPagesWithinCabinet()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var hierarchy = await CreateHierarchyAsync(page);
        var secondPage = TestNames.Prefixed("page-other");
        var explorer = page.GetByRole(AriaRole.Complementary);

        await CreatePageAsync(page, hierarchy.NotebookName, secondPage);
        await Assertions.Expect(explorer.GetByText(secondPage, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByPlaceholder("Search this cabinet…").FillAsync(hierarchy.PageTitle);

        await Assertions.Expect(explorer.GetByText(hierarchy.PageTitle, new() { Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(explorer.GetByText(secondPage, new() { Exact = true }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task NotesPage_CreateRichTextNote_OpensNewTab()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await CreateHierarchyAsync(page);
        var noteName = TestNames.Prefixed("note");

        await page.GetByRole(AriaRole.Button, new() { Name = "New Note", Exact = true }).ClickAsync();
        var noteNameInput = page.GetByPlaceholder("Untitled note");
        await noteNameInput.FillAsync(noteName);
        await noteNameInput.PressAsync("Enter");

        await Assertions.Expect(page.GetByText(noteName, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task NotesExplorer_RenameNotebook_UpdatesVisibleName()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var hierarchy = await CreateHierarchyAsync(page);
        var renamed = TestNames.Prefixed("notebook-renamed");

        var notebookRow = page.GetByRole(AriaRole.Button, new() { Name = hierarchy.NotebookName });
        await notebookRow.HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Notebook options", Exact = true }).ClickAsync();
        await page.GetByText("Rename / edit", new() { Exact = true }).ClickAsync();
        var notebookNameInput = page.GetByLabel("Notebook name");
        await notebookNameInput.FillAsync(renamed);
        await notebookNameInput.PressAsync("Enter");

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = renamed }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task NotesExplorer_DeletePage_RemovesItFromTree()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var hierarchy = await CreateHierarchyAsync(page);

        var pageRow = page.GetByRole(AriaRole.Complementary)
            .GetByText(hierarchy.PageTitle, new() { Exact = true });
        await pageRow.HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Page options", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete page" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete page" }).ClickAsync();

        await Assertions.Expect(pageRow)
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task NotesPage_DeepLinkReload_RestoresSelectedPage()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var hierarchy = await CreateHierarchyAsync(page);
        var pageUrl = page.Url;

        await page.ReloadAsync();

        Assert.Equal(pageUrl, page.Url);
        await Assertions.Expect(page.GetByRole(AriaRole.Complementary)
                .GetByText(hierarchy.PageTitle, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
                new() { Name = hierarchy.PageTitle, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task NotesPage_FavoriteAndArchiveNotebook_PersistAcrossReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var hierarchy = await CreateHierarchyAsync(page);

        await page.GetByTitle("Add to favorites").ClickAsync();
        await Assertions.Expect(page.GetByTitle("Remove from favorites"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var notebookRow = page.GetByRole(AriaRole.Button, new() { Name = hierarchy.NotebookName });
        await notebookRow.HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Notebook options", Exact = true }).ClickAsync();
        var archiveResponse = page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/content/notebooks/", StringComparison.Ordinal) &&
            response.Request.Method == "PATCH");
        await page.GetByText("Archive", new() { Exact = true }).ClickAsync();
        await archiveResponse;

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByTitle("Remove from favorites"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        notebookRow = page.GetByRole(AriaRole.Button, new() { Name = hierarchy.NotebookName });
        await notebookRow.HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Notebook options", Exact = true }).ClickAsync();
        var unarchiveResponse = page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/content/notebooks/", StringComparison.Ordinal) &&
            response.Request.Method == "PATCH");
        await page.GetByText("Unarchive", new() { Exact = true }).ClickAsync();
        await unarchiveResponse;

        await page.ReloadAsync();
        notebookRow = page.GetByRole(AriaRole.Button, new() { Name = hierarchy.NotebookName });
        await notebookRow.HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Notebook options", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Archive", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // Regression for #151: the comment-audit proxy POSTs the Yjs document
    // name (`note:<guid>` here), which the endpoint used to reject with 400,
    // so no comment ever reached the content.events bus. Drives BlockNote's
    // real "Add comment" flow on a richtext note and asserts the round-trip.
    [Fact]
    public async Task NotesPage_AddCommentOnRichTextNote_PostsCommentEventForNoteDocument()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await CreateHierarchyAsync(page);
        var noteName = TestNames.Prefixed("note");

        await page.GetByRole(AriaRole.Button, new() { Name = "New Note", Exact = true }).ClickAsync();
        var noteNameInput = page.GetByPlaceholder("Untitled note");
        await noteNameInput.FillAsync(noteName);
        await noteNameInput.PressAsync("Enter");
        await Assertions.Expect(page.GetByText(noteName, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The note tab mounts its own BlockNote editor; the page overview
        // beside it is read-only, so the editable root is unambiguous.
        var editor = page.Locator(".bn-editor[contenteditable='true']");
        await Assertions.Expect(editor).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await editor.ClickAsync();
        await page.Keyboard.TypeAsync("Comment target text");
        await page.Keyboard.PressAsync("Shift+Home");

        // Formatting toolbar appears on selection; its comment button opens
        // the floating composer (a nested editor) with focus.
        await page.GetByRole(AriaRole.Button, new() { Name = "Add comment" })
            .ClickAsync(new() { Timeout = 10_000 });
        var composer = page.Locator(".bn-comment-editor:visible").First;
        await Assertions.Expect(composer).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await composer.ClickAsync();
        await page.Keyboard.TypeAsync("First thread");

        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
            r => r.Url.EndsWith("/api/yjs/comment-event", StringComparison.Ordinal),
            new() { Timeout = 15_000 });

        Assert.Equal(204, response.Status);
        var body = response.Request.PostDataJSON();
        Assert.NotNull(body);
        Assert.StartsWith("note:", body.Value.GetProperty("documentName").GetString());
        Assert.Equal("created", body.Value.GetProperty("eventType").GetString());
        Assert.False(string.IsNullOrEmpty(body.Value.GetProperty("threadId").GetString()));
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    private static async Task<NotesHierarchy> CreateHierarchyAsync(IPage page)
    {
        var projectName = TestNames.Prefixed("notes-project");
        var cabinetName = TestNames.Prefixed("cabinet");
        var notebookName = TestNames.Prefixed("notebook");
        var pageTitle = TestNames.Prefixed("page");

        await page.GotoAsync("/projects");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add project" }).ClickAsync();
        await page.GetByPlaceholder("Acme launch").FillAsync(projectName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create project" }).ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/notes/\d+$"));

        await page.GetByRole(AriaRole.Button, new() { Name = "New cabinet" }).ClickAsync();
        await page.GetByPlaceholder("Operations").FillAsync(cabinetName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create cabinet" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = cabinetName }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await CreateNotebookAsync(page, notebookName);
        await CreatePageAsync(page, notebookName, pageTitle);
        await Assertions.Expect(page.GetByText(pageTitle, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        return new NotesHierarchy(notebookName, pageTitle);
    }

    private static async Task CreatePageAsync(IPage page, string notebookName, string pageTitle)
    {
        var notebookRow = page.GetByRole(AriaRole.Button, new() { Name = notebookName });
        await Assertions.Expect(notebookRow).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await notebookRow.HoverAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "New page", Exact = true }).ClickAsync();
        var titleInput = page.GetByPlaceholder("Untitled page");
        await titleInput.FillAsync(pageTitle);
        await titleInput.PressAsync("Enter");
    }

    private static async Task CreateNotebookAsync(IPage page, string notebookName)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "New notebook" }).ClickAsync();
        await page.GetByPlaceholder("Service Department").FillAsync(notebookName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create notebook" }).ClickAsync();
    }

    private sealed record NotesHierarchy(string NotebookName, string PageTitle);
}
