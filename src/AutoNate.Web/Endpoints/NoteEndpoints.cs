using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Notes are NOT a permissionable kind on their own — every access check is
// gated through the parent page (per design D10).
public static class NoteEndpoints
{
    public static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var pageScoped = app.MapGroup("/api/content/pages/{pageId:guid}/notes")
            .RequireAuthorization();
        var directScoped = app.MapGroup("/api/content/notes").RequireAuthorization();

        pageScoped.MapGet("/", async (
            Guid pageId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var notes = await db.Notes.AsNoTracking()
                .Where(n => n.PageId == pageId)
                .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedAtUtc)
                .ToListAsync(ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteListViewed,
                ContentResourceKinds.Note,
                resource: new { pageId },
                details: new { resultCount = notes.Count },
                ct);
            return Results.Ok(notes.Select(MapDto));
        }).RequirePermission(EntityKinds.Page, Actions.View, "pageId");

        pageScoped.MapPost("/", async (
            Guid pageId,
            CreateNoteRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!IsValidNoteKind(request.NoteKind))
                return Results.BadRequest(new { error = "noteKind must be richtext | drawing | diagram." });
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pageExists = await db.Pages.AsNoTracking().AnyAsync(p => p.Id == pageId, ct);
            if (!pageExists) return Results.BadRequest(new { error = "Page not found." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var content = string.IsNullOrWhiteSpace(request.ContentJsonb) ? "{}" : request.ContentJsonb;

            // Per-page note index = MAX existing index for this page + 1.
            // Unique (page_id, page_note_index) constraint protects against
            // the rare concurrent-insert race; the second writer fails with
            // a 23505 and the user can retry.
            var nextNoteIndex = await db.Notes.AsNoTracking()
                .Where(n => n.PageId == pageId)
                .Select(n => (int?)n.PageNoteIndex)
                .MaxAsync(ct) ?? 0;
            nextNoteIndex++;

            var note = new Note
            {
                Id = Guid.NewGuid(),
                PageId = pageId,
                NoteKind = request.NoteKind,
                Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
                ContentJsonb = content,
                CurrentVersionNumber = 2, // v1 written below; next is v2.
                PageNoteIndex = nextNoteIndex,
                SortOrder = request.SortOrder ?? 0,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Notes.Add(note);
            db.NoteVersions.Add(new NoteVersion
            {
                Id = Guid.NewGuid(),
                NoteId = note.Id,
                VersionNumber = 1,
                Title = note.Title,
                NoteKind = note.NoteKind,
                ContentJsonb = note.ContentJsonb,
                Kind = ContentVersionKinds.Manual,
                Note = "initial",
                CreatedAtUtc = now,
                CreatedBy = actorId
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteCreated,
                ContentResourceKinds.Note,
                resource: new { id = note.Id, pageId = note.PageId, noteKind = note.NoteKind, title = note.Title },
                details: null,
                ct);

            return Results.Created($"/api/content/notes/{note.Id}", MapDto(note));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Edit, "pageId");

        directScoped.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateNoteRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
            if (note is null) return Results.NotFound();
            // Re-check authorization in handler because the route doesn't
            // carry pageId — the filter ran against id, but we want Page.Edit.
            var pageDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Page, note.PageId, Actions.Edit, ct);
            if (!pageDecision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Move semantics: when PageId is supplied and differs, the caller
            // also needs Edit on the destination page. The note's
            // page_note_index is recomputed against the destination so it
            // stays unique within that page.
            Guid? previousPageId = null;
            if (request.PageId is { } newPageId && newPageId != note.PageId)
            {
                var receive = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Page, newPageId, Actions.Edit, ct);
                if (!receive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                var destExists = await db.Pages.AsNoTracking().AnyAsync(p => p.Id == newPageId, ct);
                if (!destExists) return Results.BadRequest(new { error = "Destination page not found." });
                previousPageId = note.PageId;
                note.PageId = newPageId;
                var maxIdx = await db.Notes.AsNoTracking()
                    .Where(n => n.PageId == newPageId && n.Id != id)
                    .Select(n => (int?)n.PageNoteIndex)
                    .MaxAsync(ct) ?? 0;
                note.PageNoteIndex = maxIdx + 1;
            }

            // All three note kinds are Yjs-managed (richtext/Phase 1,
            // drawing+diagram/Phase 4) — reject contentJsonb writes here
            // so a stray REST caller can't race the Hocuspocus webhook
            // snapshot.
            if (YjsManagedContentGuard.RejectYjsManagedNoteContentWrite(
                    note.NoteKind, request.ContentJsonb) is { } reject)
                return reject;

            var actorId = http.GetActorId();
            var fields = new List<string>();
            int? newVersionNumber = null;
            if (previousPageId is not null)
            {
                fields.Add("pageId");
                fields.Add("pageNoteIndex");
            }

            var contentChanging = (request.Title is not null && request.Title.Trim() != (note.Title ?? string.Empty)
                                   && !(request.Title.Trim() == string.Empty && note.Title is null))
                || (request.ContentJsonb is not null && request.ContentJsonb != note.ContentJsonb);
            if (contentChanging)
            {
                // Autosave kind enables session rollup — see ContentVersionService.
                // newVersionNumber stays null when the change folds into the
                // most recent same-author autosave row.
                newVersionNumber = await versions.SnapshotNoteBeforeChangeAsync(
                    db, note.Id, note.Title, note.NoteKind, note.ContentJsonb,
                    ContentVersionKinds.Autosave, null, actorId, DateTime.UtcNow, ct);
            }

            if (request.Title is not null)
            {
                var nt = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
                if (note.Title != nt) { note.Title = nt; fields.Add("title"); }
            }
            if (request.ContentJsonb is not null && request.ContentJsonb != note.ContentJsonb)
            {
                note.ContentJsonb = request.ContentJsonb;
                fields.Add("contentJsonb");
            }
            if (request.SortOrder is { } so && note.SortOrder != so) { note.SortOrder = so; fields.Add("sortOrder"); }

            if (fields.Count == 0) return Results.Ok(MapDto(note));

            note.UpdatedAtUtc = DateTime.UtcNow;
            note.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);

            if (newVersionNumber is { } vn)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.NoteVersionCreated,
                    ContentResourceKinds.NoteVersion,
                    resource: new { noteId = note.Id, versionNumber = vn - 1, kind = ContentVersionKinds.Autosave },
                    details: null,
                    ct);
            }
            if (previousPageId is not null)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.NoteMoved,
                    ContentResourceKinds.Note,
                    resource: new { id = note.Id },
                    details: new
                    {
                        previousPageId,
                        newPageId = note.PageId,
                        newPageNoteIndex = note.PageNoteIndex
                    },
                    ct);
            }
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteUpdated,
                ContentResourceKinds.Note,
                resource: new { id = note.Id },
                details: new { fields, newVersionNumber },
                ct);

            return Results.Ok(MapDto(note));
        }).DisableAntiforgery();

        // Copy a note to a destination page (defaults to the note's current
        // page when omitted). Requires Edit on the source page (existing) and
        // Edit on the destination page.
        directScoped.MapPost("/{id:guid}/copy", async (
            Guid id,
            CopyNoteRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var src = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
            if (src is null) return Results.NotFound();

            // Source-page Edit is needed to view+clone the note's content.
            var sourceDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Page, src.PageId, Actions.Edit, ct);
            if (!sourceDecision.IsAllowed)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var destPageId = request.PageId ?? src.PageId;
            if (destPageId != src.PageId)
            {
                var destDecision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Page, destPageId, Actions.Edit, ct);
                if (!destDecision.IsAllowed)
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var destExists = await db.Pages.AsNoTracking().AnyAsync(p => p.Id == destPageId, ct);
                if (!destExists) return Results.BadRequest(new { error = "Destination page not found." });
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var nextNoteIndex = await db.Notes.AsNoTracking()
                .Where(n => n.PageId == destPageId)
                .Select(n => (int?)n.PageNoteIndex)
                .MaxAsync(ct) ?? 0;
            nextNoteIndex++;

            var copy = new Note
            {
                Id = Guid.NewGuid(),
                PageId = destPageId,
                NoteKind = src.NoteKind,
                Title = string.IsNullOrWhiteSpace(request.Title) ? src.Title : request.Title!.Trim(),
                ContentJsonb = src.ContentJsonb,
                CurrentVersionNumber = 2,
                PageNoteIndex = nextNoteIndex,
                SortOrder = src.SortOrder,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Notes.Add(copy);
            db.NoteVersions.Add(new NoteVersion
            {
                Id = Guid.NewGuid(),
                NoteId = copy.Id,
                VersionNumber = 1,
                Title = copy.Title,
                NoteKind = copy.NoteKind,
                ContentJsonb = copy.ContentJsonb,
                Kind = ContentVersionKinds.Manual,
                Note = $"copied from {id}",
                CreatedAtUtc = now,
                CreatedBy = actorId
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Reload to materialize the DB-assigned Locator.
            var row = await db.Notes.AsNoTracking().FirstAsync(n => n.Id == copy.Id, ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteCopied,
                ContentResourceKinds.Note,
                resource: new { sourceId = id, id = row.Id, pageId = row.PageId },
                details: new { noteKind = row.NoteKind },
                ct);

            return Results.Created($"/api/content/notes/{row.Id}", MapDto(row));
        }).DisableAntiforgery();

        directScoped.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
            if (note is null) return Results.NotFound();
            // Notes are intentionally NOT subject to deletions_locked; Page.Edit
            // is the gate so callers with edit rights can manage notes even on
            // a locked project.
            var pageDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Page, note.PageId, Actions.Edit, ct);
            if (!pageDecision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            db.Notes.Remove(note);
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteDeleted,
                ContentResourceKinds.Note,
                resource: new { id, pageId = note.PageId },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery();

        return app;
    }

    private static bool IsValidNoteKind(string kind) =>
        kind == "richtext" || kind == "drawing" || kind == "diagram";

    internal static NoteDto MapDto(Note n) => new(
        n.Id, n.Locator, n.PageNoteIndex, n.PageId, n.NoteKind, n.Title, n.ContentJsonb,
        n.CurrentVersionNumber, n.SortOrder, n.IsArchived,
        n.CreatedAtUtc, n.UpdatedAtUtc, n.CreatedBy, n.UpdatedBy);

    public sealed record CreateNoteRequest(
        string NoteKind, string? Title, string? ContentJsonb, int? SortOrder);

    // PageId is optional. When non-null and different from the note's current
    // page, the note is moved (page_note_index is recomputed against the
    // destination page). Distinct sentinel for "unset" is fine here because
    // PageId is never a meaningful null on a note.
    public sealed record UpdateNoteRequest(
        string? Title, string? ContentJsonb, int? SortOrder, Guid? PageId);

    // POST /api/content/notes/{id}/copy. PageId defaults to the note's
    // current page; Title overrides the source title when provided.
    public sealed record CopyNoteRequest(Guid? PageId, string? Title);

    public sealed record NoteDto(
        Guid Id, long Locator, int PageNoteIndex, Guid PageId, string NoteKind,
        string? Title, string ContentJsonb, int CurrentVersionNumber, int SortOrder,
        bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);
}
