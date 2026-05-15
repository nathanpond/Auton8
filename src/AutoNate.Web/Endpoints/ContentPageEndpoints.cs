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
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var page = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();
            var isFavorited = await IsFavoritedAsync(db, id, http.GetActorId(), ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageViewed,
                ContentResourceKinds.Page,
                resource: new { id = page.Id, title = page.Title },
                details: null,
                ct);
            return Results.Ok(MapDto(page, isFavorited));
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
        }).DisableAntiforgery();

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
                return Results.Ok(MapDto(page, isFavoritedNoop));
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
            return Results.Ok(MapDto(page, isFavorited));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Edit);

        pages.MapDelete("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();
            db.Pages.Remove(page);
            await db.SaveChangesAsync(ct);
            await treeService.DeleteEntityAsync(db, ContentKinds.Page, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageDeleted,
                ContentResourceKinds.Page,
                resource: new { id, title = page.Title },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Delete);

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

    internal static PageDto MapDto(Page p, bool isFavorited = false) => new(
        p.Id, p.Locator, p.NotebookId, p.ParentPageId, p.Title, p.BodyJsonb,
        p.CurrentVersionNumber, p.SortOrder, p.IsArchived, isFavorited,
        p.CreatedAtUtc, p.UpdatedAtUtc, p.CreatedBy, p.UpdatedBy);

    public sealed record CreatePageRequest(
        Guid NotebookId, Guid? ParentPageId, string Title, string? BodyJsonb, int? SortOrder);

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
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record PageTreeNodeDto(
        Guid Id, long Locator, Guid NotebookId, Guid? ParentPageId, string Title,
        int SortOrder, bool IsArchived, int CurrentVersionNumber, DateTime UpdatedAtUtc);
}
