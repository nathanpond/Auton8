using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Regression coverage for archived-83: the notes/pages write API and the two
// version-restore paths shipped without a single endpoint test, and restore
// mutates user content irreversibly.
//
//   ContentPageEndpoints.cs   POST/PATCH/DELETE/copy /api/content/pages
//   NoteEndpoints.cs          POST/PATCH/DELETE/copy /api/content/notes
//   PageVersionEndpoints.cs   POST .../pages/{id}/versions/{n}/restore
//   NoteVersionEndpoints.cs   POST .../notes/{id}/versions/{n}/restore
//
// Every assertion here reads state back through the API (or through the
// version rows the API exposes) rather than trusting a status code — the
// failure mode the issue is about is "restore returned 204 and restored the
// wrong bytes", which a status-only test cannot see.
//
// Two facts about this subsystem drive the shapes below:
//
//   1. A freshly created page/note is at current_version_number = 2, not 1.
//      The create handler writes the create-time content as v1 and parks the
//      *next* number on the row (ContentPageEndpoints.cs:141,
//      NoteEndpoints.cs:78). Off-by-one here silently corrupts every later
//      snapshot, so the create tests pin it.
//   2. Body/content columns are Yjs-managed: REST PATCH of `bodyJsonb` /
//      `contentJsonb` is refused with 409 by YjsManagedContentGuard, and the
//      Hocuspocus webhook is the only writer. Tests that need a live body
//      diverging from the version history therefore write the mirror
//      directly (SetPageBodyAsync / SetNoteContentAsync), which is exactly
//      what YjsEndpoints' webhook handler does. The webhook route itself is
//      covered in YjsEndpointTests.
[Trait("Category", "Integration")]
public sealed class ContentPageWriteEndpointTests
{
    private const string BodyOne = "[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"one\"}]}]";
    private const string BodyTwo = "[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"two\"}]}]";

    // ---- pages: create -------------------------------------------------

    [Fact]
    public async Task CreatePage_PersistsBodyAndWritesInitialVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();

        var created = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        Assert.Equal("Draft one", created.Title);
        AssertJsonEquals(BodyOne, created.BodyJsonb);
        Assert.Equal(seed.NotebookId, created.NotebookId);
        // The *next* version number, with v1 already on disk.
        Assert.Equal(2, created.CurrentVersionNumber);

        var v1 = await ctx.GetPageVersionAsync(created.Id, 1);
        Assert.Equal("Draft one", v1.Title);
        AssertJsonEquals(BodyOne, v1.BodyJsonb);
        Assert.Equal(ContentVersionKinds.Manual, v1.Kind);
        Assert.Equal("initial", v1.Note);

        var list = await ctx.GetPageVersionsAsync(created.Id);
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task CreatePage_WithBlankTitle_Returns400AndCreatesNothing()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();

        var resp = await ctx.Client.PostAsJsonAsync("/api/content/pages", new
        {
            notebookId = seed.NotebookId,
            title = "   ",
            bodyJsonb = BodyOne
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(await ctx.GetPageTreeAsync(seed.NotebookId));
    }

    // The parent-page rule is what keeps the closure table sane: a page whose
    // parent lives in another notebook would sit in two subtrees at once.
    [Fact]
    public async Task CreatePage_WithParentFromAnotherNotebook_Returns400()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var foreignParent = await ctx.CreatePageAsync(seed.OtherNotebookId, "Elsewhere", "{}");

        var resp = await ctx.Client.PostAsJsonAsync("/api/content/pages", new
        {
            notebookId = seed.NotebookId,
            parentPageId = foreignParent.Id,
            title = "Child",
            bodyJsonb = "{}"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(await ctx.GetPageTreeAsync(seed.NotebookId));
    }

    // ---- pages: update -------------------------------------------------

    // YjsManagedContentGuard: a REST body write must be refused loudly, not
    // silently dropped, and above all must not land — it would race the
    // Hocuspocus snapshot and lose the collaborative state.
    [Fact]
    public async Task PatchPage_WithBodyJsonb_Returns409AndLeavesBodyIntact()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        var resp = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { bodyJsonb = BodyTwo });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains("bodyJsonb", await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var after = await ctx.GetPageAsync(page.Id);
        AssertJsonEquals(BodyOne, after.BodyJsonb);
        Assert.Equal(2, after.CurrentVersionNumber);
    }

    [Fact]
    public async Task PatchPage_TitleChange_SnapshotsPriorTitleAsNewVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        var resp = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { title = "Draft two" });
        resp.EnsureSuccessStatusCode();
        var updated = await resp.Content.ReadFromJsonAsync<ContentPageEndpoints.PageDto>();

        Assert.NotNull(updated);
        Assert.Equal("Draft two", updated!.Title);
        Assert.Equal(3, updated.CurrentVersionNumber);

        // The snapshot captures the state *before* the edit, so v2 is the old
        // title — restoring it has to give "Draft one" back.
        var v2 = await ctx.GetPageVersionAsync(page.Id, 2);
        Assert.Equal("Draft one", v2.Title);
        AssertJsonEquals(BodyOne, v2.BodyJsonb);
        Assert.Equal(ContentVersionKinds.Autosave, v2.Kind);
    }

    // Session rollup: a second autosave by the same author inside the session
    // gap folds into the existing row. Without this the history fills with one
    // entry per keystroke-debounce and the restore list becomes unusable.
    [Fact]
    public async Task PatchPage_SecondEditWithinSession_RollsUpWithoutNewVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        (await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { title = "Draft two" }))
            .EnsureSuccessStatusCode();
        (await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { title = "Draft three" }))
            .EnsureSuccessStatusCode();

        var list = await ctx.GetPageVersionsAsync(page.Id);
        Assert.Equal(2, list.TotalCount);
        Assert.Equal(
            new[] { 2, 1 },
            list.Items.Select(i => i.VersionNumber).ToArray());

        var after = await ctx.GetPageAsync(page.Id);
        Assert.Equal("Draft three", after.Title);
        Assert.Equal(3, after.CurrentVersionNumber);

        // Still the pre-session state, not the intermediate "Draft two".
        var v2 = await ctx.GetPageVersionAsync(page.Id, 2);
        Assert.Equal("Draft one", v2.Title);
    }

    [Fact]
    public async Task PatchPage_MoveToAnotherNotebook_RelocatesThePageTreeRow()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Movable", BodyOne);

        var resp = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { notebookId = seed.OtherNotebookId });
        resp.EnsureSuccessStatusCode();
        var moved = await resp.Content.ReadFromJsonAsync<ContentPageEndpoints.PageDto>();

        Assert.Equal(seed.OtherNotebookId, moved!.NotebookId);
        Assert.Empty(await ctx.GetPageTreeAsync(seed.NotebookId));
        Assert.Equal(page.Id, Assert.Single(await ctx.GetPageTreeAsync(seed.OtherNotebookId)).Id);
    }

    // ---- pages: delete / copy ------------------------------------------

    [Fact]
    public async Task DeletePage_CascadesToChildPagesAndNotes()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var parent = await ctx.CreatePageAsync(seed.NotebookId, "Parent", BodyOne);
        var child = await ctx.CreatePageAsync(seed.NotebookId, "Child", BodyTwo, parent.Id);
        await ctx.CreateNoteAsync(child.Id, "richtext", "Child note", BodyOne);

        var resp = await ctx.Client.DeleteAsync($"/api/content/pages/{parent.Id}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Empty(await ctx.GetPageTreeAsync(seed.NotebookId));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ctx.Client.GetAsync($"/api/content/pages/{child.Id}")).StatusCode);
        Assert.Equal(0, await ctx.CountNotesForPageAsync(child.Id));
    }

    [Fact]
    public async Task CopyPage_ClonesBodyNotesAndDescendantsIntoDestination()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var source = await ctx.CreatePageAsync(seed.NotebookId, "Original", BodyOne);
        var child = await ctx.CreatePageAsync(seed.NotebookId, "Original child", BodyTwo, source.Id);
        await ctx.CreateNoteAsync(source.Id, "richtext", "Kept note", BodyTwo);

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/pages/{source.Id}/copy",
            new { notebookId = seed.OtherNotebookId, title = "Duplicate" });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var copy = await resp.Content.ReadFromJsonAsync<ContentPageEndpoints.PageDto>();
        Assert.NotNull(copy);
        Assert.NotEqual(source.Id, copy!.Id);
        Assert.Equal("Duplicate", copy.Title);
        AssertJsonEquals(BodyOne, copy.BodyJsonb);
        Assert.Equal(seed.OtherNotebookId, copy.NotebookId);
        Assert.Equal(2, copy.CurrentVersionNumber);

        var copiedNotes = await ctx.GetNotesAsync(copy.Id);
        var copiedNote = Assert.Single(copiedNotes);
        Assert.Equal("Kept note", copiedNote.Title);
        AssertJsonEquals(BodyTwo, copiedNote.ContentJsonb);
        Assert.Equal(1, copiedNote.PageNoteIndex);

        // The descendant travels with the copy, and the source is untouched.
        var destTree = await ctx.GetPageTreeAsync(seed.OtherNotebookId);
        Assert.Equal(2, destTree.Count);
        Assert.Contains(destTree, p => p.ParentPageId == copy.Id);
        Assert.Equal(2, (await ctx.GetPageTreeAsync(seed.NotebookId)).Count);
        Assert.Equal("Original", (await ctx.GetPageAsync(source.Id)).Title);
        Assert.Equal("Original child", (await ctx.GetPageAsync(child.Id)).Title);
    }

    // ---- pages: version restore ----------------------------------------

    // The core of archived-83. Restore must put the *target version's* bytes back on
    // the live row — both fields, not just the title the history list shows.
    [Fact]
    public async Task RestorePageVersion_RestoresThatVersionsTitleAndBody()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        // Simulate the collaborative edit that only the Yjs webhook can make,
        // then rename so the live row differs from v1 in both fields.
        await ctx.SetPageBodyAsync(page.Id, BodyTwo);
        (await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { title = "Draft two" }))
            .EnsureSuccessStatusCode();

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/pages/{page.Id}/versions/1/restore", new { note = "back to v1" });

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var after = await ctx.GetPageAsync(page.Id);
        Assert.Equal("Draft one", after.Title);
        AssertJsonEquals(BodyOne, after.BodyJsonb);
    }

    // A restore is itself a mutation, so it snapshots the pre-restore state
    // as a kind='restore' row. Losing that makes restore a one-way door.
    [Fact]
    public async Task RestorePageVersion_SnapshotsPreRestoreStateAndIsReversible()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);
        await ctx.SetPageBodyAsync(page.Id, BodyTwo);
        (await ctx.Client.PatchAsJsonAsync(
            $"/api/content/pages/{page.Id}", new { title = "Draft two" }))
            .EnsureSuccessStatusCode();

        // current_version_number is 3 here, so the restore parks the live
        // state at v3 and moves the row to 4.
        (await ctx.Client.PostAsJsonAsync(
            $"/api/content/pages/{page.Id}/versions/1/restore", new { note = "back to v1" }))
            .EnsureSuccessStatusCode();

        var snapshot = await ctx.GetPageVersionAsync(page.Id, 3);
        Assert.Equal(ContentVersionKinds.Restore, snapshot.Kind);
        Assert.Equal("back to v1", snapshot.Note);
        Assert.Equal("Draft two", snapshot.Title);
        AssertJsonEquals(BodyTwo, snapshot.BodyJsonb);
        Assert.Equal(4, (await ctx.GetPageAsync(page.Id)).CurrentVersionNumber);

        (await ctx.Client.PostAsJsonAsync(
            $"/api/content/pages/{page.Id}/versions/3/restore", new { note = "undo" }))
            .EnsureSuccessStatusCode();

        var undone = await ctx.GetPageAsync(page.Id);
        Assert.Equal("Draft two", undone.Title);
        AssertJsonEquals(BodyTwo, undone.BodyJsonb);
    }

    [Fact]
    public async Task RestorePageVersion_UnknownVersion_Returns404AndLeavesPageUnchanged()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/pages/{page.Id}/versions/99/restore", new { note = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var after = await ctx.GetPageAsync(page.Id);
        Assert.Equal("Draft one", after.Title);
        AssertJsonEquals(BodyOne, after.BodyJsonb);
        // No snapshot row was written for the failed attempt.
        Assert.Equal(2, after.CurrentVersionNumber);
        Assert.Equal(1, (await ctx.GetPageVersionsAsync(page.Id)).TotalCount);
    }

    // Pruning must never remove the version the live row is currently at —
    // that would strand the page with no restorable history entry for its own
    // content.
    [Fact]
    public async Task DeletePageVersion_CurrentVersion_Returns409AndKeepsIt()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Draft one", BodyOne);

        var resp = await ctx.Client.DeleteAsync(
            $"/api/content/pages/{page.Id}/versions/1");

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains(
            "current version",
            await resp.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, (await ctx.GetPageVersionsAsync(page.Id)).TotalCount);
    }

    // ---- notes ---------------------------------------------------------

    [Fact]
    public async Task CreateNote_PersistsContentAndWritesInitialVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");

        var note = await ctx.CreateNoteAsync(page.Id, "richtext", "First note", BodyOne);

        Assert.Equal("First note", note.Title);
        AssertJsonEquals(BodyOne, note.ContentJsonb);
        Assert.Equal("richtext", note.NoteKind);
        Assert.Equal(1, note.PageNoteIndex);
        Assert.Equal(2, note.CurrentVersionNumber);

        var v1 = await ctx.GetNoteVersionAsync(note.Id, 1);
        Assert.Equal("First note", v1.Title);
        AssertJsonEquals(BodyOne, v1.ContentJsonb);
        Assert.Equal("richtext", v1.NoteKind);
        Assert.Equal(ContentVersionKinds.Manual, v1.Kind);
        Assert.Equal("initial", v1.Note);
    }

    // page_note_index is the second segment of a note's URL, so a duplicate or
    // a reset to 1 breaks every existing deep link into the page.
    [Fact]
    public async Task CreateNote_AssignsSequentialPageNoteIndexPerPage()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");

        var first = await ctx.CreateNoteAsync(page.Id, "richtext", "One", BodyOne);
        var second = await ctx.CreateNoteAsync(page.Id, "drawing", "Two", BodyTwo);

        Assert.Equal(1, first.PageNoteIndex);
        Assert.Equal(2, second.PageNoteIndex);
    }

    [Fact]
    public async Task CreateNote_WithUnknownKind_Returns400AndCreatesNothing()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/pages/{page.Id}/notes",
            new { noteKind = "spreadsheet", title = "Nope", contentJsonb = BodyOne });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(await ctx.GetNotesAsync(page.Id));
    }

    // All three note kinds are Yjs-managed (richtext since Phase 1, drawing and
    // diagram since Phase 4). A regression that lets any one of them through
    // REST would silently lose whatever the live Y.Doc holds.
    [Theory]
    [InlineData("richtext")]
    [InlineData("drawing")]
    [InlineData("diagram")]
    public async Task PatchNote_WithContentJsonb_Returns409AndLeavesContentIntact(string noteKind)
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var note = await ctx.CreateNoteAsync(page.Id, noteKind, "Note", BodyOne);

        var resp = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/notes/{note.Id}", new { contentJsonb = BodyTwo });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains("contentJsonb", await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var after = Assert.Single(await ctx.GetNotesAsync(page.Id));
        AssertJsonEquals(BodyOne, after.ContentJsonb);
        Assert.Equal(2, after.CurrentVersionNumber);
    }

    // previewSvg is a derived snapshot: meaningless on richtext (rejected
    // loudly), and on drawing/diagram it must land without spending a version
    // or touching UpdatedAt — otherwise the idle-snapshot writes bury real
    // edits in the activity feed.
    [Fact]
    public async Task PatchNote_PreviewSvg_RejectedOnRichtextAndStoredWithoutVersionOnDrawing()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var richtext = await ctx.CreateNoteAsync(page.Id, "richtext", "Rich", BodyOne);
        var drawing = await ctx.CreateNoteAsync(page.Id, "drawing", "Draw", BodyTwo);

        var rejected = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/notes/{richtext.Id}", new { previewSvg = "<svg/>" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var accepted = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/notes/{drawing.Id}", new { previewSvg = "<svg id=\"d\"/>" });
        accepted.EnsureSuccessStatusCode();

        var notes = await ctx.GetNotesAsync(page.Id);
        var storedRichtext = notes.Single(n => n.Id == richtext.Id);
        var storedDrawing = notes.Single(n => n.Id == drawing.Id);
        Assert.Null(storedRichtext.PreviewSvg);
        Assert.Equal("<svg id=\"d\"/>", storedDrawing.PreviewSvg);
        // No version was spent on the snapshot write.
        Assert.Equal(2, storedDrawing.CurrentVersionNumber);
        Assert.Equal(1, (await ctx.GetNoteVersionsAsync(drawing.Id)).TotalCount);
    }

    [Fact]
    public async Task PatchNote_TitleChange_SnapshotsPriorTitleAsNewVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var note = await ctx.CreateNoteAsync(page.Id, "richtext", "Old title", BodyOne);

        var resp = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/notes/{note.Id}", new { title = "New title" });
        resp.EnsureSuccessStatusCode();
        var updated = await resp.Content.ReadFromJsonAsync<NoteEndpoints.NoteDto>();

        Assert.Equal("New title", updated!.Title);
        Assert.Equal(3, updated.CurrentVersionNumber);

        var v2 = await ctx.GetNoteVersionAsync(note.Id, 2);
        Assert.Equal("Old title", v2.Title);
        AssertJsonEquals(BodyOne, v2.ContentJsonb);
        Assert.Equal(ContentVersionKinds.Autosave, v2.Kind);
    }

    [Fact]
    public async Task PatchNote_MoveToAnotherPage_ReindexesWithinDestination()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var source = await ctx.CreatePageAsync(seed.NotebookId, "Source", "{}");
        var dest = await ctx.CreatePageAsync(seed.NotebookId, "Dest", "{}");
        await ctx.CreateNoteAsync(dest.Id, "richtext", "Sitting tenant", BodyOne);
        var moving = await ctx.CreateNoteAsync(source.Id, "richtext", "Mover", BodyTwo);

        var resp = await ctx.Client.PatchAsJsonAsync(
            $"/api/content/notes/{moving.Id}", new { pageId = dest.Id });
        resp.EnsureSuccessStatusCode();
        var moved = await resp.Content.ReadFromJsonAsync<NoteEndpoints.NoteDto>();

        Assert.Equal(dest.Id, moved!.PageId);
        // Renumbered against the destination page, not carried over as 1.
        Assert.Equal(2, moved.PageNoteIndex);
        Assert.Empty(await ctx.GetNotesAsync(source.Id));
        Assert.Equal(2, (await ctx.GetNotesAsync(dest.Id)).Count);
    }

    [Fact]
    public async Task CopyNote_ClonesContentWithFreshInitialVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var note = await ctx.CreateNoteAsync(page.Id, "diagram", "Diagram", BodyOne);

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/notes/{note.Id}/copy", new { title = "Diagram copy" });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var copy = await resp.Content.ReadFromJsonAsync<NoteEndpoints.NoteDto>();
        Assert.NotEqual(note.Id, copy!.Id);
        Assert.Equal("Diagram copy", copy.Title);
        Assert.Equal("diagram", copy.NoteKind);
        AssertJsonEquals(BodyOne, copy.ContentJsonb);
        Assert.Equal(2, copy.PageNoteIndex);
        Assert.Equal(2, copy.CurrentVersionNumber);

        // The copy gets its own history rather than sharing the source's.
        var v1 = await ctx.GetNoteVersionAsync(copy.Id, 1);
        AssertJsonEquals(BodyOne, v1.ContentJsonb);
        Assert.Equal($"copied from {note.Id}", v1.Note);
        Assert.Equal(1, (await ctx.GetNoteVersionsAsync(copy.Id)).TotalCount);
    }

    [Fact]
    public async Task DeleteNote_RemovesItFromThePage()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var doomed = await ctx.CreateNoteAsync(page.Id, "richtext", "Doomed", BodyOne);
        var kept = await ctx.CreateNoteAsync(page.Id, "richtext", "Kept", BodyTwo);

        var resp = await ctx.Client.DeleteAsync($"/api/content/notes/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var remaining = Assert.Single(await ctx.GetNotesAsync(page.Id));
        Assert.Equal(kept.Id, remaining.Id);
    }

    // ---- notes: version restore ----------------------------------------

    // Note restore is the other half of archived-83, and it has an extra trap: note
    // kind is immutable post-create, so a restore must move title + content
    // and leave note_kind alone.
    [Fact]
    public async Task RestoreNoteVersion_RestoresThatVersionsTitleAndContent()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var note = await ctx.CreateNoteAsync(page.Id, "richtext", "Old title", BodyOne);

        await ctx.SetNoteContentAsync(note.Id, BodyTwo);
        (await ctx.Client.PatchAsJsonAsync(
            $"/api/content/notes/{note.Id}", new { title = "New title" }))
            .EnsureSuccessStatusCode();

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/notes/{note.Id}/versions/1/restore", new { note = "rollback" });

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        var after = Assert.Single(await ctx.GetNotesAsync(page.Id));
        Assert.Equal("Old title", after.Title);
        AssertJsonEquals(BodyOne, after.ContentJsonb);
        Assert.Equal("richtext", after.NoteKind);
        Assert.Equal(4, after.CurrentVersionNumber);

        var snapshot = await ctx.GetNoteVersionAsync(note.Id, 3);
        Assert.Equal(ContentVersionKinds.Restore, snapshot.Kind);
        Assert.Equal("rollback", snapshot.Note);
        Assert.Equal("New title", snapshot.Title);
        AssertJsonEquals(BodyTwo, snapshot.ContentJsonb);
    }

    [Fact]
    public async Task RestoreNoteVersion_UnknownVersion_Returns404AndLeavesNoteUnchanged()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedTreeAsync();
        var page = await ctx.CreatePageAsync(seed.NotebookId, "Host", "{}");
        var note = await ctx.CreateNoteAsync(page.Id, "richtext", "Only title", BodyOne);

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/notes/{note.Id}/versions/42/restore", new { note = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var after = Assert.Single(await ctx.GetNotesAsync(page.Id));
        Assert.Equal("Only title", after.Title);
        AssertJsonEquals(BodyOne, after.ContentJsonb);
        Assert.Equal(2, after.CurrentVersionNumber);
        Assert.Equal(1, (await ctx.GetNoteVersionsAsync(note.Id)).TotalCount);
    }

    [Fact]
    public async Task RestoreNoteVersion_UnknownNote_Returns404()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.SeedTreeAsync();

        var resp = await ctx.Client.PostAsJsonAsync(
            $"/api/content/notes/{Guid.NewGuid()}/versions/1/restore", new { note = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- fixture -------------------------------------------------------

    private sealed record SeededTree(
        Guid ProjectId, Guid CabinetId, Guid NotebookId, Guid OtherNotebookId, Guid ActorId);

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(AutoNateWebApplicationFactory factory, HttpClient client)
        {
            Factory = factory;
            Client = client;
        }

        public AutoNateWebApplicationFactory Factory { get; }
        public HttpClient Client { get; }

        public static async Task<TestContext> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync(
                new Dictionary<string, string?>
                {
                    // These handlers gate on IContentAuthorizer (and the page
                    // DELETE on IsProjectOwnerAsync), which enforces regardless
                    // of Authorization:Enabled — the seeded admin needs
                    // SuperAdmin standing for the write paths to be reachable.
                    ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
                });
            var client = factory.CreateClient();
            // Dev auto-login skips POSTs — land the auth cookie with a GET first.
            (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
            return new TestContext(factory, client);
        }

        // Project → cabinet → two notebooks, with the closure rows the
        // authorizer walks. Pages and notes below are created through the API
        // under test, never seeded.
        public async Task<SeededTree> SeedTreeAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var tree = sp.GetRequiredService<IContentTreeService>();

            Guid actorId;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                actorId = await db.LocalUsers.AsNoTracking()
                    .Where(u => u.Username == "admin")
                    .Select(u => u.UserId)
                    .FirstAsync();
            }

            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(), Name = "page-write-tests",
                DeletionsLocked = false, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var cabinet = new Cabinet
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, Name = "cab",
                IsArchived = false, SortOrder = 0,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var notebook = new Notebook
            {
                Id = Guid.NewGuid(), CabinetId = cabinet.Id, Name = "nb",
                IsArchived = false, SortOrder = 0,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var otherNotebook = new Notebook
            {
                Id = Guid.NewGuid(), CabinetId = cabinet.Id, Name = "nb-2",
                IsArchived = false, SortOrder = 1,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Cabinets.Add(cabinet);
                db.Notebooks.Add(notebook);
                db.Notebooks.Add(otherNotebook);
                await db.SaveChangesAsync();
            }

            foreach (var (kind, id) in new[]
            {
                (ContentKinds.Project, project.Id),
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Notebook, notebook.Id),
                (ContentKinds.Notebook, otherNotebook.Id)
            })
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
            }

            return new SeededTree(project.Id, cabinet.Id, notebook.Id, otherNotebook.Id, actorId);
        }

        public async Task<ContentPageEndpoints.PageDto> CreatePageAsync(
            Guid notebookId, string title, string bodyJsonb, Guid? parentPageId = null)
        {
            var resp = await Client.PostAsJsonAsync("/api/content/pages", new
            {
                notebookId,
                parentPageId,
                title,
                bodyJsonb
            });
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<ContentPageEndpoints.PageDto>())!;
        }

        public async Task<NoteEndpoints.NoteDto> CreateNoteAsync(
            Guid pageId, string noteKind, string? title, string contentJsonb)
        {
            var resp = await Client.PostAsJsonAsync(
                $"/api/content/pages/{pageId}/notes",
                new { noteKind, title, contentJsonb });
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<NoteEndpoints.NoteDto>())!;
        }

        public async Task<ContentPageEndpoints.PageDto> GetPageAsync(Guid pageId)
        {
            var resp = await Client.GetAsync($"/api/content/pages/{pageId}");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<ContentPageEndpoints.PageDto>())!;
        }

        public async Task<List<ContentPageEndpoints.PageTreeNodeDto>> GetPageTreeAsync(Guid notebookId)
        {
            var resp = await Client.GetAsync($"/api/content/notebooks/{notebookId}/page-tree");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<List<ContentPageEndpoints.PageTreeNodeDto>>())!;
        }

        public async Task<List<NoteEndpoints.NoteDto>> GetNotesAsync(Guid pageId)
        {
            var resp = await Client.GetAsync($"/api/content/pages/{pageId}/notes");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<List<NoteEndpoints.NoteDto>>())!;
        }

        public async Task<PageVersionEndpoints.PageVersionDto> GetPageVersionAsync(Guid pageId, int n)
        {
            var resp = await Client.GetAsync($"/api/content/pages/{pageId}/versions/{n}");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<PageVersionEndpoints.PageVersionDto>())!;
        }

        public async Task<PageVersionEndpoints.PageVersionPageResponse> GetPageVersionsAsync(Guid pageId)
        {
            var resp = await Client.GetAsync($"/api/content/pages/{pageId}/versions/");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<PageVersionEndpoints.PageVersionPageResponse>())!;
        }

        public async Task<NoteVersionEndpoints.NoteVersionDto> GetNoteVersionAsync(Guid noteId, int n)
        {
            var resp = await Client.GetAsync($"/api/content/notes/{noteId}/versions/{n}");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<NoteVersionEndpoints.NoteVersionDto>())!;
        }

        public async Task<NoteVersionEndpoints.NoteVersionPageResponse> GetNoteVersionsAsync(Guid noteId)
        {
            var resp = await Client.GetAsync($"/api/content/notes/{noteId}/versions/");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<NoteVersionEndpoints.NoteVersionPageResponse>())!;
        }

        public async Task<int> CountNotesForPageAsync(Guid pageId)
        {
            using var scope = Factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Notes.AsNoTracking().CountAsync(n => n.PageId == pageId);
        }

        // Stands in for the Hocuspocus snapshot webhook: page bodies are
        // Yjs-managed and REST refuses to write them, so this is the only way
        // to make the live row diverge from its version history.
        public async Task SetPageBodyAsync(Guid pageId, string bodyJsonb)
        {
            using var scope = Factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var page = await db.Pages.FirstAsync(p => p.Id == pageId);
            page.BodyJsonb = bodyJsonb;
            await db.SaveChangesAsync();
        }

        public async Task SetNoteContentAsync(Guid noteId, string contentJsonb)
        {
            using var scope = Factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var note = await db.Notes.FirstAsync(n => n.Id == noteId);
            note.ContentJsonb = contentJsonb;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // Postgres reserializes jsonb on the way out: `{"a":1}` comes back as
    // `{"a": 1}`, and object keys are re-emitted in its own order (by key
    // length, then bytes) rather than the order they were written in.
    // Comparing raw strings would be testing Postgres's formatter rather than
    // whether the endpoint stored what it was given, so both sides are
    // canonicalized — whitespace normalized and object keys sorted, all the
    // way down — before comparison.
    private static void AssertJsonEquals(string expected, string? actual)
    {
        Assert.NotNull(actual);
        using var expectedDoc = JsonDocument.Parse(expected);
        using var actualDoc = JsonDocument.Parse(actual!);
        Assert.Equal(Canonicalize(expectedDoc.RootElement), Canonicalize(actualDoc.RootElement));
    }

    private static string Canonicalize(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var members = element.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => $"{JsonSerializer.Serialize(p.Name)}:{Canonicalize(p.Value)}");
                return "{" + string.Join(",", members) + "}";
            case JsonValueKind.Array:
                // Array order is meaningful in document content — preserved.
                return "[" + string.Join(",", element.EnumerateArray().Select(Canonicalize)) + "]";
            default:
                return element.GetRawText();
        }
    }

}
