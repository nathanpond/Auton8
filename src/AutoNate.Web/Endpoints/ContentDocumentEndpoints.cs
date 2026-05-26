using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Document CRUD for the Documents subsystem (Phase 2).
// Project → Folder (self-nesting) → Document. Templates and documents share
// this endpoint; the `kind` field on each row discriminates ('document' vs
// 'template'). Phase 2 ships read/write REST access; Phase 3 layers the
// docx-editor + Hocuspocus on top — that's why bodyJsonb is freely writable
// from REST here (no YjsManagedContentGuard yet, unlike pages).
//
// Permissions:
//   list  — filtered by IContentAuthorizer.GetAllowedIdsAsync(Document.View)
//   get   — RequirePermission(Document.View)
//   create— gated on the parent's Edit action: Project.Edit when at the
//           project root, Folder.Edit when nested inside a folder. (Same
//           D9 "Edit-on-parent gates child creation" pattern as cabinets +
//           folders.)
//   patch — RequirePermission(Document.Edit); a move to a different parent
//           additionally re-checks Edit on the new parent.
//   delete— RequirePermission(Document.Delete); honors deletions_locked
//           via IContentAuthorizer.
public static class ContentDocumentEndpoints
{
    public static IEndpointRouteBuilder MapContentDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/documents").RequireAuthorization();

        // Paged list. Filters keep response shape consistent with folders:
        //   projectId      — scope to a single project
        //   folderId       — scope to a single folder (nested children only)
        //   atProjectRoot  — true => folder_id IS NULL within the project
        //   kind           — 'document' | 'template' filter for the gallery
        //   includeArchived — defaults to false so the picker view stays clean
        group.MapGet("/page", async (
            Guid? projectId,
            Guid? folderId,
            bool? atProjectRoot,
            string? kind,
            bool? includeArchived,
            int? page,
            int? pageSize,
            string? q,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var access = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Document, Actions.View, ct);

            var query = db.Documents.AsNoTracking().AsQueryable();
            if (projectId is { } pid) query = query.Where(d => d.ProjectId == pid);
            if (folderId is { } fid) query = query.Where(d => d.FolderId == fid);
            if (atProjectRoot == true) query = query.Where(d => d.FolderId == null);
            if (kind is not null && DocumentKinds.IsValid(kind))
            {
                query = query.Where(d => d.Kind == kind);
            }
            if (includeArchived != true)
            {
                query = query.Where(d => !d.IsArchived);
            }
            if (!access.Unrestricted)
            {
                var ids = access.AllowedIds;
                query = query.Where(d => ids.Contains(d.Id));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                query = query.Where(d =>
                    EF.Functions.ILike(d.Title, "%" + needle + "%") ||
                    (d.Description != null && EF.Functions.ILike(d.Description, "%" + needle + "%")));
            }

            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 200);
            var items = await query
                .OrderBy(d => d.SortOrder).ThenBy(d => d.Title)
                .Skip(pg * ps).Take(ps)
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentListViewed,
                ContentResourceKinds.Document,
                resource: projectId is { } ? new { projectId = projectId.Value, folderId, kind } : null,
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps },
                ct);

            return Results.Ok(new DocumentPageResponse(items.Select(MapDto).ToList(), totalCount));
        }).AuthorizedInHandler(
            "Result set filtered by GetAllowedIdsAsync(Document.View); " +
            "unauthorized documents never enter the response.");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
            if (doc is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentViewed,
                ContentResourceKinds.Document,
                resource: new { id = doc.Id, title = doc.Title, kind = doc.Kind },
                details: null,
                ct);
            return Results.Ok(MapDto(doc));
        }).RequirePermission(EntityKinds.Document, Actions.View);

        group.MapPost("/", async (
            CreateDocumentRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new { error = "Document title is required." });
            }
            var kind = string.IsNullOrWhiteSpace(request.Kind)
                ? DocumentKinds.Document
                : request.Kind!.Trim();
            if (!DocumentKinds.IsValid(kind))
            {
                return Results.BadRequest(new { error = $"Unknown document kind '{kind}'." });
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var projectExists = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists) return Results.BadRequest(new { error = "Project not found." });

            // Authorize against the parent — D9 composition. Root documents
            // gate on Project.Edit; nested documents gate on Folder.Edit, and
            // the parent folder must belong to the same project.
            AuthDecision decision;
            if (request.FolderId is { } folderId)
            {
                var folder = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == folderId)
                    .Select(f => new { f.Id, f.ProjectId })
                    .FirstOrDefaultAsync(ct);
                if (folder is null) return Results.BadRequest(new { error = "Folder not found." });
                if (folder.ProjectId != request.ProjectId)
                {
                    return Results.BadRequest(new { error = "Folder belongs to a different project." });
                }
                decision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Folder, folderId, Actions.Edit, ct);
            }
            else
            {
                decision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Project, request.ProjectId, Actions.Edit, ct);
            }
            if (!decision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Optional template back-reference. If supplied, the template
            // must exist AND belong to the same project (templates aren't
            // cross-project in v2; that limitation can relax later).
            if (request.TemplateId is { } templateId)
            {
                var template = await db.Documents.AsNoTracking()
                    .Where(d => d.Id == templateId)
                    .Select(d => new { d.Id, d.ProjectId, d.Kind })
                    .FirstOrDefaultAsync(ct);
                if (template is null) return Results.BadRequest(new { error = "Template not found." });
                if (template.Kind != DocumentKinds.Template)
                {
                    return Results.BadRequest(new { error = "Referenced row is not a template." });
                }
            }

            var actorId = http.GetActorId();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var doc = new Document
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                FolderId = request.FolderId,
                Kind = kind,
                TemplateId = request.TemplateId,
                Title = request.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                BodyJsonb = string.IsNullOrWhiteSpace(request.BodyJsonb) ? "{}" : request.BodyJsonb!,
                CurrentVersionNumber = 1,
                SortOrder = request.SortOrder ?? 0,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, doc.Id, ct);

            // Record the initial state as version 1 with kind='manual' so the
            // version history is never empty (mirrors how pages get an initial
            // version row). Skips the autosave session-rollup path because
            // 'manual' always writes a fresh row.
            var initialVersion = await versions.SnapshotDocumentBeforeChangeAsync(
                db, doc.Id, doc.Title, doc.BodyJsonb,
                ContentVersionKinds.Manual, "Initial version", actorId, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentCreated,
                ContentResourceKinds.Document,
                resource: new
                {
                    id = doc.Id,
                    projectId = doc.ProjectId,
                    folderId = doc.FolderId,
                    title = doc.Title
                },
                details: new { kind = doc.Kind, templateId = doc.TemplateId },
                ct);
            if (initialVersion is { } v)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.DocumentVersionCreated,
                    ContentResourceKinds.DocumentVersion,
                    resource: new { documentId = doc.Id, versionNumber = v, kind = ContentVersionKinds.Manual },
                    details: null,
                    ct);
            }

            return Results.Created($"/api/content/documents/{doc.Id}", MapDto(doc));
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "AuthorizeAsync on the parent gates child creation: " +
              "Project.Edit for root documents, Folder.Edit for nested " +
              "(composes the kind-level Create with per-resource Edit per design D9).");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateDocumentRequest request,
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
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (doc is null) return Results.NotFound();

            var fields = new List<string>();
            string? archiveEventType = null;
            Guid? previousProjectId = null;
            Guid? previousFolderId = null;
            int? newVersionNumber = null;
            var movedParent = false;

            string priorTitle = doc.Title;
            string priorBody = doc.BodyJsonb;
            var contentChanging = (request.Title is not null && request.Title.Trim() != doc.Title)
                || (request.BodyJsonb is not null && request.BodyJsonb != doc.BodyJsonb);
            if (contentChanging)
            {
                // Session-rollup autosave: when the most recent row is a
                // same-author autosave inside SessionGap, returns null and
                // doesn't bump CurrentVersionNumber.
                newVersionNumber = await versions.SnapshotDocumentBeforeChangeAsync(
                    db, doc.Id, priorTitle, priorBody,
                    ContentVersionKinds.Autosave, null, actorId, DateTime.UtcNow, ct);
            }

            if (request.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                    return Results.BadRequest(new { error = "Document title cannot be empty." });
                if (doc.Title != request.Title.Trim()) { doc.Title = request.Title.Trim(); fields.Add("title"); }
            }
            if (request.Description is not null)
            {
                var nd = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (doc.Description != nd) { doc.Description = nd; fields.Add("description"); }
            }
            if (request.BodyJsonb is not null && request.BodyJsonb != doc.BodyJsonb)
            {
                doc.BodyJsonb = request.BodyJsonb;
                fields.Add("bodyJsonb");
            }
            if (request.SortOrder is { } so && doc.SortOrder != so) { doc.SortOrder = so; fields.Add("sortOrder"); }
            if (request.IsArchived is { } archived && archived != doc.IsArchived)
            {
                doc.IsArchived = archived;
                fields.Add("isArchived");
                archiveEventType = archived
                    ? ContentEventTypes.DocumentArchived
                    : ContentEventTypes.DocumentRestored;
            }

            // Move semantics — same as folders. Cross-project moves require
            // a fresh folder_id (or null for project root) so the document
            // can't dangle into the old project's folder tree.
            if (request.ProjectId is { } newProjectId && newProjectId != doc.ProjectId)
            {
                var receive = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Project, newProjectId, Actions.Edit, ct);
                if (!receive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

                previousProjectId = doc.ProjectId;
                doc.ProjectId = newProjectId;
                fields.Add("projectId");
                movedParent = true;
                if (request.FolderId is null && doc.FolderId is not null && !request.FolderIdSet)
                {
                    return Results.BadRequest(new
                    {
                        error = "When moving a document across projects, folderId must be sent " +
                                "(null for project root) so it doesn't dangle into the previous project."
                    });
                }
            }

            if (request.FolderIdSet)
            {
                var newFolder = request.FolderId;
                if (newFolder != doc.FolderId)
                {
                    if (newFolder is { } nf)
                    {
                        var folderRow = await db.Folders.AsNoTracking()
                            .Where(f => f.Id == nf)
                            .Select(f => new { f.Id, f.ProjectId })
                            .FirstOrDefaultAsync(ct);
                        if (folderRow is null) return Results.BadRequest(new { error = "Folder not found." });
                        if (folderRow.ProjectId != doc.ProjectId)
                        {
                            return Results.BadRequest(new
                            {
                                error = "Folder belongs to a different project than the destination."
                            });
                        }
                        var receiveFolder = await authorizer.AuthorizeAsync(
                            http.User, ContentKinds.Folder, nf, Actions.Edit, ct);
                        if (!receiveFolder.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                    else
                    {
                        var receiveRoot = await authorizer.AuthorizeAsync(
                            http.User, ContentKinds.Project, doc.ProjectId, Actions.Edit, ct);
                        if (!receiveRoot.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                    previousFolderId = doc.FolderId;
                    doc.FolderId = newFolder;
                    fields.Add("folderId");
                    movedParent = true;
                }
            }

            if (fields.Count == 0)
            {
                return Results.Ok(MapDto(doc));
            }

            doc.UpdatedAtUtc = DateTime.UtcNow;
            doc.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);
            if (movedParent)
            {
                await treeService.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Document, doc.Id, ct);
            }
            await tx.CommitAsync(ct);

            if (movedParent)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.DocumentMoved,
                    ContentResourceKinds.Document,
                    resource: new { id = doc.Id },
                    details: new
                    {
                        previousProjectId,
                        newProjectId = doc.ProjectId,
                        previousFolderId,
                        newFolderId = doc.FolderId
                    },
                    ct);
            }
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                archiveEventType ?? ContentEventTypes.DocumentUpdated,
                ContentResourceKinds.Document,
                resource: new { id = doc.Id, title = doc.Title },
                details: new { fields, newVersionNumber },
                ct);
            if (newVersionNumber is { } nv)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.DocumentVersionCreated,
                    ContentResourceKinds.DocumentVersion,
                    resource: new { documentId = doc.Id, versionNumber = nv, kind = ContentVersionKinds.Autosave },
                    details: null,
                    ct);
            }

            return Results.Ok(MapDto(doc));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (doc is null) return Results.NotFound();
            db.Documents.Remove(doc);
            await db.SaveChangesAsync(ct);
            await treeService.DeleteEntityAsync(db, ContentKinds.Document, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentDeleted,
                ContentResourceKinds.Document,
                resource: new { id, title = doc.Title, kind = doc.Kind },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Delete);

        return app;
    }

    internal static DocumentDto MapDto(Document d) => new(
        d.Id, d.Locator, d.ProjectId, d.FolderId, d.Kind, d.TemplateId,
        d.Title, d.Description, d.BodyJsonb, d.CurrentVersionNumber,
        d.SortOrder, d.IsArchived,
        d.CreatedAtUtc, d.UpdatedAtUtc, d.CreatedBy, d.UpdatedBy);

    public sealed record CreateDocumentRequest(
        Guid ProjectId, Guid? FolderId, string? Kind, Guid? TemplateId,
        string Title, string? Description, string? BodyJsonb, int? SortOrder);

    // FolderId uses an explicit "Set" flag so callers can disambiguate
    // "leave alone" (omit) from "move to project root" (FolderId=null +
    // FolderIdSet=true). Matches the pattern ContentPageEndpoints uses for
    // ParentPageId.
    public sealed class UpdateDocumentRequest
    {
        public Guid? ProjectId { get; set; }
        public Guid? FolderId { get; set; }
        public bool FolderIdSet { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? BodyJsonb { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsArchived { get; set; }
    }

    public sealed record DocumentDto(
        Guid Id, long Locator, Guid ProjectId, Guid? FolderId,
        string Kind, Guid? TemplateId,
        string Title, string? Description, string BodyJsonb, int CurrentVersionNumber,
        int SortOrder, bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record DocumentPageResponse(List<DocumentDto> Items, int TotalCount);
}
