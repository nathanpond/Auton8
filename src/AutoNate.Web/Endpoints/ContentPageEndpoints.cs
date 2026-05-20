using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Content pages (Project → Cabinet → Notebook → Page). Distinct from the
// existing PageEndpoints which serves the SPA's registered-page registry.
public static class ContentPageEndpoints
{
    public static IEndpointRouteBuilder MapContentPageEndpoints(this IEndpointRouteBuilder app)
    {
        var pages = app.MapGroup("/api/content/pages").RequireAuthorization();
        var notebookScoped = app.MapGroup("/api/content/notebooks").RequireAuthorization();

        // Page tree for a notebook. Returns the actor-visible subset of the
        // notebook's pages with parent/sort metadata so the SPA can render the
        // tree without further requests.
        notebookScoped.MapGet("/{id:guid}/page-tree", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var access = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Page, Actions.View, ct);

            var query = db.Pages.AsNoTracking().Where(p => p.NotebookId == id);
            if (!access.Unrestricted)
            {
                var ids = access.AllowedIds;
                query = query.Where(p => ids.Contains(p.Id));
            }
            var pages = await query
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                .Select(p => new PageTreeNodeDto(
                    p.Id, p.Locator, p.NotebookId, p.ParentPageId, p.Title, p.SortOrder,
                    p.IsArchived, p.CurrentVersionNumber, p.UpdatedAtUtc))
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageTreeViewed,
                ContentResourceKinds.Page,
                resource: new { notebookId = id },
                details: new { pageCount = pages.Count },
                ct);

            return Results.Ok(pages);
        }).RequirePermission(EntityKinds.Notebook, Actions.View, "id");

        pages.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var page = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();
            var isFavorited = await IsFavoritedAsync(db, id, http.GetActorId(), ct);
            // The Move / Copy / Delete affordances in the SPA ellipsis menu
            // need the viewer's owner status for the page's project. Owner
            // gates Delete (UI hide + API gate); Move/Copy themselves are
            // gated by Edit on the destination, but the menu surfaces them
            // alongside Delete so the SPA computes "isOwner" here too.
            var projectId = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == ContentKinds.Page
                             && ca.DescendantId == id
                             && ca.AncestorKind == ContentKinds.Project)
                .Select(ca => (Guid?)ca.AncestorId)
                .FirstOrDefaultAsync(ct);
            var actorIsProjectOwner = projectId is { } pid
                && await authorizer.IsProjectOwnerAsync(http.User, pid, ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageViewed,
                ContentResourceKinds.Page,
                resource: new { id = page.Id, title = page.Title },
                details: null,
                ct);
            return Results.Ok(MapDto(page, isFavorited, actorIsProjectOwner));
        }).RequirePermission(EntityKinds.Page, Actions.View);

        pages.MapPost("/", async (
            CreatePageRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "Page title is required." });

            // Edit on the parent notebook is required. If parent_page_id is
            // also supplied, Edit on that page is required as well (the parent
            // page itself must be writable to attach children).
            var notebookDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Notebook, request.NotebookId, Actions.Edit, ct);
            if (!notebookDecision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (request.ParentPageId is { } parent)
            {
                var parentDecision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Page, parent, Actions.Edit, ct);
                if (!parentDecision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var notebookExists = await db.Notebooks.AsNoTracking()
                .AnyAsync(n => n.Id == request.NotebookId, ct);
            if (!notebookExists) return Results.BadRequest(new { error = "Notebook not found." });
            if (request.ParentPageId is { } pp)
            {
                var parentOk = await db.Pages.AsNoTracking()
                    .AnyAsync(p => p.Id == pp && p.NotebookId == request.NotebookId, ct);
                if (!parentOk)
                {
                    return Results.BadRequest(new { error = "Parent page must belong to the same notebook." });
                }
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var body = string.IsNullOrWhiteSpace(request.BodyJsonb) ? "{}" : request.BodyJsonb;
            var page = new Page
            {
                Id = Guid.NewGuid(),
                NotebookId = request.NotebookId,
                ParentPageId = request.ParentPageId,
                Title = request.Title.Trim(),
                BodyJsonb = body,
                CurrentVersionNumber = 2, // v1 is written below; the *next* version is v2.
                SortOrder = request.SortOrder ?? 0,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Pages.Add(page);
            // Initial version row captures the create-time content as v1.
            db.PageVersions.Add(new PageVersion
            {
                Id = Guid.NewGuid(),
                PageId = page.Id,
                VersionNumber = 1,
                Title = page.Title,
                BodyJsonb = page.BodyJsonb,
                Kind = ContentVersionKinds.Manual,
                Note = "initial",
                CreatedAtUtc = now,
                CreatedBy = actorId
            });
            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Page, page.Id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageCreated,
                ContentResourceKinds.Page,
                resource: new
                {
                    id = page.Id,
                    notebookId = page.NotebookId,
                    parentPageId = page.ParentPageId,
                    title = page.Title
                },
                details: null,
                ct);

            return Results.Created($"/api/content/pages/{page.Id}", MapDto(page));
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "AuthorizeAsync(Notebook.Edit) on the parent notebook (and " +
              "AuthorizeAsync(Page.Edit) on the parent page when nesting) " +
              "gates child creation per design D9.");

        pages.MapPatch("/{id:guid}", async (
            Guid id,
            UpdatePageRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Page bodies are Yjs-managed in Phase 1; reject any direct
            // bodyJsonb write so a stray REST caller can't race the
            // Hocuspocus webhook snapshot.
            if (YjsManagedContentGuard.RejectPageBodyWrite(request.BodyJsonb) is { } reject)
                return reject;

            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();

            var fields = new List<string>();
            string? archiveEventType = null;
            Guid? previousNotebookId = null;
            Guid? previousParentPageId = null;
            int? newVersionNumber = null;

            // Snapshot prior content if title or body is changing.
            string priorTitle = page.Title;
            string priorBody = page.BodyJsonb;
            var contentChanging = (request.Title is not null && request.Title.Trim() != page.Title)
                || (request.BodyJsonb is not null && request.BodyJsonb != page.BodyJsonb);
            if (contentChanging)
            {
                // Autosave kind drives the session-rollup path: when the most
                // recent row is a same-author autosave within the session
                // gap, no new version is written and newVersionNumber stays
                // null. The PageVersionCreated audit event below is gated on
                // a non-null result.
                newVersionNumber = await versions.SnapshotPageBeforeChangeAsync(
                    db, page.Id, priorTitle, priorBody,
                    ContentVersionKinds.Autosave, null, actorId, DateTime.UtcNow, ct);
            }

            if (request.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                    return Results.BadRequest(new { error = "Page title cannot be empty." });
                if (page.Title != request.Title.Trim()) { page.Title = request.Title.Trim(); fields.Add("title"); }
            }
            if (request.BodyJsonb is not null && request.BodyJsonb != page.BodyJsonb)
            {
                page.BodyJsonb = request.BodyJsonb;
                fields.Add("bodyJsonb");
            }
            if (request.SortOrder is { } so && page.SortOrder != so) { page.SortOrder = so; fields.Add("sortOrder"); }
            if (request.IsArchived is { } archived && archived != page.IsArchived)
            {
                page.IsArchived = archived;
                fields.Add("isArchived");
                archiveEventType = archived
                    ? ContentEventTypes.PageArchived
                    : ContentEventTypes.PageRestored;
            }

            // Move semantics. Notebook change requires Edit on the new notebook.
            // Parent-page change requires Edit on the new parent page (when not
            // null) and must belong to the new (or unchanged) notebook.
            if (request.NotebookId is { } newNotebookId && newNotebookId != page.NotebookId)
            {
                var receive = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Notebook, newNotebookId, Actions.Edit, ct);
                if (!receive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                previousNotebookId = page.NotebookId;
                page.NotebookId = newNotebookId;
                fields.Add("notebookId");
            }
            if (request.ParentPageIdSet)
            {
                var requested = request.ParentPageId;
                if (requested == page.Id)
                {
                    return Results.BadRequest(new { error = "A page cannot be its own parent." });
                }
                if (requested is { } newParent)
                {
                    var parentReceive = await authorizer.AuthorizeAsync(
                        http.User, ContentKinds.Page, newParent, Actions.Edit, ct);
                    if (!parentReceive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                    var parentRow = await db.Pages.AsNoTracking()
                        .Where(p => p.Id == newParent)
                        .Select(p => new { p.NotebookId })
                        .FirstOrDefaultAsync(ct);
                    if (parentRow is null)
                    {
                        return Results.BadRequest(new { error = "Parent page not found." });
                    }
                    if (parentRow.NotebookId != page.NotebookId)
                    {
                        return Results.BadRequest(new { error = "Parent page must belong to the same notebook." });
                    }
                }
                if (page.ParentPageId != requested)
                {
                    previousParentPageId = page.ParentPageId;
                    page.ParentPageId = requested;
                    fields.Add("parentPageId");
                }
            }

            if (fields.Count == 0)
            {
                await tx.RollbackAsync(ct);
                var isFavoritedNoop = await IsFavoritedAsync(db, page.Id, actorId, ct);
                var ownerNoop = await ResolveActorOwnerAsync(db, authorizer, http.User, page.Id, ct);
                return Results.Ok(MapDto(page, isFavoritedNoop, ownerNoop));
            }

            page.UpdatedAtUtc = DateTime.UtcNow;
            page.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);
            if (previousNotebookId is not null || previousParentPageId is not null)
            {
                await treeService.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Page, page.Id, ct);
            }
            await tx.CommitAsync(ct);

            if (newVersionNumber is { } vn)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.PageVersionCreated,
                    ContentResourceKinds.PageVersion,
                    resource: new { pageId = page.Id, versionNumber = vn - 1, kind = ContentVersionKinds.Autosave },
                    details: null,
                    ct);
            }
            if (previousNotebookId is not null || previousParentPageId is not null)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.PageMoved,
                    ContentResourceKinds.Page,
                    resource: new { id = page.Id },
                    details: new
                    {
                        previousNotebookId,
                        newNotebookId = page.NotebookId,
                        previousParentPageId,
                        newParentPageId = page.ParentPageId
                    },
                    ct);
            }
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                archiveEventType ?? ContentEventTypes.PageUpdated,
                ContentResourceKinds.Page,
                resource: new { id = page.Id, title = page.Title },
                details: new { fields, newVersionNumber },
                ct);

            var isFavorited = await IsFavoritedAsync(db, page.Id, http.GetActorId(), ct);
            var owner = await ResolveActorOwnerAsync(db, authorizer, http.User, page.Id, ct);
            return Results.Ok(MapDto(page, isFavorited, owner));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Edit);

        // Owner-only. Gated in-handler against IContentAuthorizer.IsProjectOwnerAsync
        // rather than via RequirePermission(Page, Delete) — the ellipsis menu's
        // Delete is intentionally narrower than Contributor's "can delete content"
        // permission. Cascades down the page subtree via FK ON DELETE CASCADE
        // (notes + child pages); we also scrub any per-page permission_grants
        // for the page itself and its descendant pages so stale `/page/{id}`
        // allow grants don't linger.
        pages.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();

            // Resolve the page's project for the owner check. If the closure
            // row is missing (shouldn't happen for a real page) the request is
            // refused — fail closed.
            var projectId = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == ContentKinds.Page
                             && ca.DescendantId == id
                             && ca.AncestorKind == ContentKinds.Project)
                .Select(ca => (Guid?)ca.AncestorId)
                .FirstOrDefaultAsync(ct);
            if (projectId is null)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            if (!await authorizer.IsProjectOwnerAsync(http.User, projectId.Value, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            // Every descendant page id (depth ≥ 1 in the closure under this
            // page). FK cascade will drop these rows; we need the ids first
            // to scrub their per-page permission_grants and closure rows.
            var descendantPageIds = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.AncestorKind == ContentKinds.Page
                             && ca.AncestorId == id
                             && ca.DescendantKind == ContentKinds.Page
                             && ca.Depth > 0)
                .Select(ca => ca.DescendantId)
                .ToListAsync(ct);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Scrub `/page/{guid}` allow/deny grants for this page and every
            // descendant page. ContentShareEndpoints writes these in canonical
            // `/page/{guid}` form; we match by selector_string equality.
            var pageSelectors = new List<string>(descendantPageIds.Count + 1)
            {
                $"/page/{id}"
            };
            foreach (var dpid in descendantPageIds)
            {
                pageSelectors.Add($"/page/{dpid}");
            }
            await db.PermissionGrants
                .Where(pg => pageSelectors.Contains(pg.SelectorString))
                .ExecuteDeleteAsync(ct);

            // Closure rows for descendant pages won't be cleaned by the FK
            // cascade (content_ancestors has no FK back to pages). Wipe them
            // explicitly so the closure doesn't accumulate orphans.
            foreach (var dpid in descendantPageIds)
            {
                await treeService.DeleteEntityAsync(db, ContentKinds.Page, dpid, ct);
            }

            db.Pages.Remove(page);
            await db.SaveChangesAsync(ct);
            await treeService.DeleteEntityAsync(db, ContentKinds.Page, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageDeleted,
                ContentResourceKinds.Page,
                resource: new { id, title = page.Title },
                details: new { descendantPageCount = descendantPageIds.Count },
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "IsProjectOwnerAsync gates the delete; intentionally narrower " +
              "than Page.Delete because the ellipsis-menu Delete cascades " +
              "child pages and notes via FK CASCADE.");

        // Copy a page (and every descendant page + every contained note) to
        // a destination notebook (and optionally under a parent page). The
        // copy is gated by Edit on the destination notebook AND Edit on the
        // optional parent page — the same rules CreatePage applies.
        //
        // View permission on the source page is implicitly required by the
        // RequirePermission filter below; if the caller can't see the source
        // they get 403 before the handler runs.
        //
        // Body content is cloned verbatim (Yjs payloads are JSON-stringified
        // and immutable from this endpoint's POV). A fresh v1 "initial copy"
        // version row is written per cloned page/note so the new entity has
        // a clean version history that doesn't reference the source's
        // history graph.
        pages.MapPost("/{id:guid}/copy", async (
            Guid id,
            CopyPageRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var src = await db.Pages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (src is null) return Results.NotFound();

            // Destination authorization mirrors CreatePage.
            var notebookDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Notebook, request.NotebookId, Actions.Edit, ct);
            if (!notebookDecision.IsAllowed)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (request.ParentPageId is { } parent)
            {
                if (parent == id)
                {
                    return Results.BadRequest(new { error = "A page cannot be copied under itself." });
                }
                var parentDecision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Page, parent, Actions.Edit, ct);
                if (!parentDecision.IsAllowed)
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var parentRow = await db.Pages.AsNoTracking()
                    .Where(p => p.Id == parent)
                    .Select(p => new { p.NotebookId })
                    .FirstOrDefaultAsync(ct);
                if (parentRow is null)
                {
                    return Results.BadRequest(new { error = "Parent page not found." });
                }
                if (parentRow.NotebookId != request.NotebookId)
                {
                    return Results.BadRequest(new { error = "Parent page must belong to the destination notebook." });
                }
            }
            else
            {
                var notebookExists = await db.Notebooks.AsNoTracking()
                    .AnyAsync(n => n.Id == request.NotebookId, ct);
                if (!notebookExists)
                    return Results.BadRequest(new { error = "Notebook not found." });
            }

            // Refuse copies where the destination parent page lives inside the
            // source's own subtree — that would create a cycle on the copy
            // OR (more subtly) place the new copy under itself, an outcome
            // the user almost certainly didn't intend. Cheap check: walk the
            // closure rows.
            if (request.ParentPageId is { } pp2)
            {
                var inOwnSubtree = await db.ContentAncestors.AsNoTracking()
                    .AnyAsync(ca => ca.AncestorKind == ContentKinds.Page
                                 && ca.AncestorId == id
                                 && ca.DescendantKind == ContentKinds.Page
                                 && ca.DescendantId == pp2, ct);
                if (inOwnSubtree)
                {
                    return Results.BadRequest(new { error = "Destination cannot be inside the page being copied." });
                }
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;

            // Collect every page in the source subtree once.
            var subtreePages = await db.Pages.AsNoTracking()
                .Where(p => p.Id == id
                            || db.ContentAncestors.Any(ca =>
                                ca.AncestorKind == ContentKinds.Page
                                && ca.AncestorId == id
                                && ca.DescendantKind == ContentKinds.Page
                                && ca.DescendantId == p.Id))
                .ToListAsync(ct);

            // Collect all notes whose parent page is in the subtree.
            var subtreePageIds = subtreePages.Select(p => p.Id).ToHashSet();
            var subtreeNotes = await db.Notes.AsNoTracking()
                .Where(n => subtreePageIds.Contains(n.PageId))
                .ToListAsync(ct);

            // Map old page ids to fresh ones so we can preserve the parent
            // lineage in the copy.
            var pageIdMap = subtreePages.ToDictionary(p => p.Id, _ => Guid.NewGuid());

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Insert pages depth-first by closure depth so parents are written
            // before children. We rely on the fact that subtreePages contains
            // self + every descendant; the root copy adopts the requested
            // notebook + optional parent page, and inner pages keep their
            // (remapped) parent ids inside the subtree.
            // Pre-load each subtree page's depth-from-root via the closure.
            var depthByPageId = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.AncestorKind == ContentKinds.Page
                             && ca.AncestorId == id
                             && ca.DescendantKind == ContentKinds.Page)
                .Select(ca => new { ca.DescendantId, ca.Depth })
                .ToListAsync(ct);
            var depthMap = depthByPageId.ToDictionary(d => d.DescendantId, d => d.Depth);
            // The source page itself sits at depth 0 in its own subtree.
            depthMap[id] = 0;
            var pagesInOrder = subtreePages
                .OrderBy(p => depthMap.TryGetValue(p.Id, out var d) ? d : int.MaxValue)
                .ToList();

            Page? rootCopy = null;
            foreach (var p in pagesInOrder)
            {
                var newId = pageIdMap[p.Id];
                Guid? newParent;
                Guid newNotebookId;
                if (p.Id == id)
                {
                    newParent = request.ParentPageId;
                    newNotebookId = request.NotebookId;
                }
                else
                {
                    // p.ParentPageId is in pageIdMap by construction (its
                    // parent is somewhere in the subtree). If it isn't we
                    // fall through to null parent + destination notebook,
                    // which preserves reachability if the closure is partial.
                    newParent = p.ParentPageId is { } op && pageIdMap.TryGetValue(op, out var mappedParent)
                        ? mappedParent
                        : request.ParentPageId;
                    newNotebookId = request.NotebookId;
                }
                var copy = new Page
                {
                    Id = newId,
                    NotebookId = newNotebookId,
                    ParentPageId = newParent,
                    Title = p.Id == id && !string.IsNullOrWhiteSpace(request.Title)
                        ? request.Title!.Trim()
                        : p.Title,
                    BodyJsonb = p.BodyJsonb,
                    CurrentVersionNumber = 2,
                    SortOrder = p.SortOrder,
                    IsArchived = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedBy = actorId,
                    UpdatedBy = actorId
                };
                db.Pages.Add(copy);
                db.PageVersions.Add(new PageVersion
                {
                    Id = Guid.NewGuid(),
                    PageId = newId,
                    VersionNumber = 1,
                    Title = copy.Title,
                    BodyJsonb = copy.BodyJsonb,
                    Kind = ContentVersionKinds.Manual,
                    Note = $"copied from {p.Id}",
                    CreatedAtUtc = now,
                    CreatedBy = actorId
                });
                if (p.Id == id) rootCopy = copy;
            }
            await db.SaveChangesAsync(ct);

            // Notes are cloned per source page with fresh page_note_index
            // numbering scoped to each copied page.
            var noteIndexByCopyPageId = new Dictionary<Guid, int>();
            foreach (var n in subtreeNotes)
            {
                if (!pageIdMap.TryGetValue(n.PageId, out var copyPageId)) continue;
                if (!noteIndexByCopyPageId.TryGetValue(copyPageId, out var idx)) idx = 0;
                idx++;
                noteIndexByCopyPageId[copyPageId] = idx;
                var noteId = Guid.NewGuid();
                var noteCopy = new Note
                {
                    Id = noteId,
                    PageId = copyPageId,
                    NoteKind = n.NoteKind,
                    Title = n.Title,
                    ContentJsonb = n.ContentJsonb,
                    CurrentVersionNumber = 2,
                    PageNoteIndex = idx,
                    SortOrder = n.SortOrder,
                    IsArchived = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedBy = actorId,
                    UpdatedBy = actorId
                };
                db.Notes.Add(noteCopy);
                db.NoteVersions.Add(new NoteVersion
                {
                    Id = Guid.NewGuid(),
                    NoteId = noteId,
                    VersionNumber = 1,
                    Title = noteCopy.Title,
                    NoteKind = noteCopy.NoteKind,
                    ContentJsonb = noteCopy.ContentJsonb,
                    Kind = ContentVersionKinds.Manual,
                    Note = $"copied from {n.Id}",
                    CreatedAtUtc = now,
                    CreatedBy = actorId
                });
            }
            await db.SaveChangesAsync(ct);

            // Closure rows for every copied page.
            foreach (var p in pagesInOrder)
            {
                await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Page, pageIdMap[p.Id], ct);
            }

            await tx.CommitAsync(ct);

            // Reload to pick up the DB-assigned Locator for the response DTO.
            var rootRow = await db.Pages.AsNoTracking()
                .FirstAsync(p => p.Id == rootCopy!.Id, ct);
            var owner = await ResolveActorOwnerAsync(db, authorizer, http.User, rootRow.Id, ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageCopied,
                ContentResourceKinds.Page,
                resource: new { sourceId = id, id = rootRow.Id, title = rootRow.Title },
                details: new
                {
                    notebookId = rootRow.NotebookId,
                    parentPageId = rootRow.ParentPageId,
                    copiedPageCount = pagesInOrder.Count,
                    copiedNoteCount = subtreeNotes.Count
                },
                ct);

            return Results.Created($"/api/content/pages/{rootRow.Id}",
                MapDto(rootRow, isFavorited: false, owner));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.View);

        // Favorite/unfavorite a page for the current user. Per-user state, so
        // the row is keyed (page_id, user_id). PUT is idempotent via ON
        // CONFLICT DO NOTHING; DELETE silently no-ops when the row is absent.
        // View permission is the gate — if you can read the page, you can
        // bookmark it for your own dashboard.
        pages.MapPut("/{id:guid}/favorite", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var page = await db.Pages.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.Title })
                .FirstOrDefaultAsync(ct);
            if (page is null) return Results.NotFound();
            var actorId = http.GetActorId();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO page_favorites (page_id, user_id, favorited_at_utc)
                   VALUES ({id}, {actorId}, {DateTime.UtcNow})
                   ON CONFLICT (page_id, user_id) DO NOTHING",
                ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageFavorited,
                ContentResourceKinds.Page,
                resource: new { id = page.Id, title = page.Title },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.View);

        pages.MapDelete("/{id:guid}/favorite", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var page = await db.Pages.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.Title })
                .FirstOrDefaultAsync(ct);
            if (page is null) return Results.NotFound();
            var actorId = http.GetActorId();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM page_favorites
                   WHERE page_id = {id} AND user_id = {actorId}",
                ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageUnfavorited,
                ContentResourceKinds.Page,
                resource: new { id = page.Id, title = page.Title },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.View);

        return app;
    }

    private static Task<bool> IsFavoritedAsync(AutoNateDbContext db, Guid pageId, Guid userId, CancellationToken ct) =>
        userId == Guid.Empty
            ? Task.FromResult(false)
            : db.PageFavorites.AsNoTracking()
                .AnyAsync(f => f.PageId == pageId && f.UserId == userId, ct);

    // Resolve whether the caller is the owner of the project containing the
    // page, for use in MapDto's ActorIsProjectOwner flag. Returns false when
    // the page has no project ancestor row yet — that is a transient state
    // during create and the SPA will refetch.
    private static async Task<bool> ResolveActorOwnerAsync(
        AutoNateDbContext db,
        IContentAuthorizer authorizer,
        System.Security.Claims.ClaimsPrincipal user,
        Guid pageId,
        CancellationToken ct)
    {
        var projectId = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == ContentKinds.Page
                         && ca.DescendantId == pageId
                         && ca.AncestorKind == ContentKinds.Project)
            .Select(ca => (Guid?)ca.AncestorId)
            .FirstOrDefaultAsync(ct);
        if (projectId is null) return false;
        return await authorizer.IsProjectOwnerAsync(user, projectId.Value, ct);
    }

    internal static PageDto MapDto(Page p, bool isFavorited = false, bool actorIsProjectOwner = false) => new(
        p.Id, p.Locator, p.NotebookId, p.ParentPageId, p.Title, p.BodyJsonb,
        p.CurrentVersionNumber, p.SortOrder, p.IsArchived, isFavorited,
        actorIsProjectOwner,
        p.CreatedAtUtc, p.UpdatedAtUtc, p.CreatedBy, p.UpdatedBy);

    public sealed record CreatePageRequest(
        Guid NotebookId, Guid? ParentPageId, string Title, string? BodyJsonb, int? SortOrder);

    // Destination for POST /api/content/pages/{id}/copy. Title is optional —
    // when omitted the copy reuses the source's title verbatim.
    public sealed record CopyPageRequest(
        Guid NotebookId, Guid? ParentPageId, string? Title);

    // ParentPageIdSet is required to distinguish "leave parent unchanged"
    // (caller omits the field) from "set parent to null" (caller sets null).
    public sealed record UpdatePageRequest
    {
        public Guid? NotebookId { get; init; }
        public string? Title { get; init; }
        public string? BodyJsonb { get; init; }
        public int? SortOrder { get; init; }
        public bool? IsArchived { get; init; }
        public Guid? ParentPageId { get; init; }
        public bool ParentPageIdSet { get; init; }
    }

    public sealed record PageDto(
        Guid Id, long Locator, Guid NotebookId, Guid? ParentPageId, string Title,
        string BodyJsonb, int CurrentVersionNumber, int SortOrder, bool IsArchived,
        bool IsFavorited,
        // True when the calling user is the project owner (or wildcard /
        // super-admin equivalent). Drives the SPA ellipsis menu's Delete
        // gate, matching the page DELETE endpoint's owner check.
        bool ActorIsProjectOwner,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record PageTreeNodeDto(
        Guid Id, long Locator, Guid NotebookId, Guid? ParentPageId, string Title,
        int SortOrder, bool IsArchived, int CurrentVersionNumber, DateTime UpdatedAtUtc);
}
