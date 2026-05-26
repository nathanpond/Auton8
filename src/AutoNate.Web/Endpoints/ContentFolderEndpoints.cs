using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Folder CRUD for the Documents subsystem (Phase 1).
// Project → Folder (self-nesting, unlimited depth) → Document. Documents
// land in a later phase; this file gives the SPA something to render in the
// folder tree + breadcrumb on /documents/p/:projectId so the UI is testable
// before any editor ships.
//
// Permission shape mirrors CabinetEndpoints.cs:
//   list  — filtered by IContentAuthorizer.GetAllowedIdsAsync(Folder.View)
//   get   — RequirePermission(Folder.View)
//   create— gated on the parent's Edit action: Project.Edit when creating a
//           top-level folder, Folder.Edit when nesting. (Same "Edit-on-parent
//           gates child creation" pattern as cabinets — design D9.)
//   patch — RequirePermission(Folder.Edit); a move to a different parent
//           additionally re-checks Edit on the new parent.
//   delete— RequirePermission(Folder.Delete); honors deletions_locked via
//           IContentAuthorizer (Folder is not a KindIsAlwaysDeletable kind).
public static class ContentFolderEndpoints
{
    public static IEndpointRouteBuilder MapContentFolderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/folders").RequireAuthorization();

        // Paged list. The SPA folder tree paginates per-project for the
        // root-level folders and per-parent for nested folders, so the
        // optional projectId + parentFolderId + atProjectRoot filters keep
        // the response shape consistent with cabinets/notebooks.
        group.MapGet("/page", async (
            Guid? projectId,
            Guid? parentFolderId,
            bool? atProjectRoot,
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
                http.User, ContentKinds.Folder, Actions.View, ct);

            var query = db.Folders.AsNoTracking().AsQueryable();
            if (projectId is { } pid) query = query.Where(f => f.ProjectId == pid);
            // Distinguish "nested under this folder" from "at the project root."
            // The two filters are mutually exclusive — passing both ANDs them
            // into a query that always returns zero rows, which is a useful
            // signal during integration but never a deliberate caller intent.
            if (parentFolderId is { } pfid) query = query.Where(f => f.ParentFolderId == pfid);
            if (atProjectRoot == true) query = query.Where(f => f.ParentFolderId == null);
            if (!access.Unrestricted)
            {
                var ids = access.AllowedIds;
                query = query.Where(f => ids.Contains(f.Id));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                query = query.Where(f =>
                    EF.Functions.ILike(f.Name, "%" + needle + "%") ||
                    (f.Description != null && EF.Functions.ILike(f.Description, "%" + needle + "%")));
            }

            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 200);
            var items = await query
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
                .Skip(pg * ps).Take(ps)
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.FolderListViewed,
                ContentResourceKinds.Folder,
                resource: projectId is { } ? new { projectId = projectId.Value, parentFolderId } : null,
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps },
                ct);

            return Results.Ok(new FolderPageResponse(items.Select(MapDto).ToList(), totalCount));
        }).AuthorizedInHandler(
            "Result set filtered by GetAllowedIdsAsync(Folder.View); " +
            "unauthorized folders never enter the response.");

        // Returns the direct children of a folder — sub-folders + documents
        // in one envelope. The Drive-style grid in the SPA iterates each
        // array independently (folders render as folder cards, documents as
        // document rows), which is why this returns two arrays instead of
        // a discriminated-union array.
        group.MapGet("/{id:guid}/children", async (
            Guid id,
            bool? includeArchived,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var parent = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (parent is null) return Results.NotFound();

            var folderAccess = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Folder, Actions.View, ct);
            var folderQuery = db.Folders.AsNoTracking()
                .Where(f => f.ParentFolderId == id);
            if (!folderAccess.Unrestricted)
            {
                var ids = folderAccess.AllowedIds;
                folderQuery = folderQuery.Where(f => ids.Contains(f.Id));
            }
            var folders = await folderQuery
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
                .ToListAsync(ct);

            var docAccess = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Document, Actions.View, ct);
            var docQuery = db.Documents.AsNoTracking()
                .Where(d => d.FolderId == id);
            if (includeArchived != true)
            {
                docQuery = docQuery.Where(d => !d.IsArchived);
            }
            if (!docAccess.Unrestricted)
            {
                var ids = docAccess.AllowedIds;
                docQuery = docQuery.Where(d => ids.Contains(d.Id));
            }
            var documents = await docQuery
                .OrderBy(d => d.SortOrder).ThenBy(d => d.Title)
                .ToListAsync(ct);

            return Results.Ok(new FolderChildrenResponse(
                folders.Select(MapDto).ToList(),
                documents.Select(ContentDocumentEndpoints.MapDto).ToList()));
        }).RequirePermission(EntityKinds.Folder, Actions.View);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (folder is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.FolderViewed,
                ContentResourceKinds.Folder,
                resource: new { id = folder.Id, name = folder.Name },
                details: null,
                ct);
            return Results.Ok(MapDto(folder));
        }).RequirePermission(EntityKinds.Folder, Actions.View);

        group.MapPost("/", async (
            CreateFolderRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Folder name is required." });
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var projectExists = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists) return Results.BadRequest(new { error = "Project not found." });

            // Authorize against the parent — same D9 composition cabinets use.
            // Root folders gate on Project.Edit; nested folders gate on
            // Folder.Edit (and the parent must belong to the same project).
            AuthDecision decision;
            if (request.ParentFolderId is { } parentId)
            {
                var parent = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == parentId)
                    .Select(f => new { f.Id, f.ProjectId })
                    .FirstOrDefaultAsync(ct);
                if (parent is null) return Results.BadRequest(new { error = "Parent folder not found." });
                if (parent.ProjectId != request.ProjectId)
                {
                    return Results.BadRequest(new { error = "Parent folder belongs to a different project." });
                }
                decision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Folder, parentId, Actions.Edit, ct);
            }
            else
            {
                decision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Project, request.ProjectId, Actions.Edit, ct);
            }
            if (!decision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var actorId = http.GetActorId();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var folder = new Folder
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                ParentFolderId = request.ParentFolderId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim(),
                SortOrder = request.SortOrder ?? 0,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Folders.Add(folder);
            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Folder, folder.Id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.FolderCreated,
                ContentResourceKinds.Folder,
                resource: new
                {
                    id = folder.Id,
                    projectId = folder.ProjectId,
                    parentFolderId = folder.ParentFolderId,
                    name = folder.Name
                },
                details: null,
                ct);

            return Results.Created($"/api/content/folders/{folder.Id}", MapDto(folder));
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "AuthorizeAsync on the parent gates child creation: Project.Edit " +
              "for root folders, Folder.Edit for nested folders (composes the " +
              "kind-level Create with per-resource Edit per design D9).");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateFolderRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (folder is null) return Results.NotFound();

            var fields = new List<string>();
            string? archiveEventType = null;
            Guid? previousProjectId = null;
            Guid? previousParentFolderId = null;
            var movedParent = false;

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(new { error = "Folder name cannot be empty." });
                if (folder.Name != request.Name.Trim()) { folder.Name = request.Name.Trim(); fields.Add("name"); }
            }
            if (request.Description is not null)
            {
                var nd = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (folder.Description != nd) { folder.Description = nd; fields.Add("description"); }
            }
            if (request.Icon is not null)
            {
                var ni = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
                if (folder.Icon != ni) { folder.Icon = ni; fields.Add("icon"); }
            }
            if (request.SortOrder is { } so && folder.SortOrder != so) { folder.SortOrder = so; fields.Add("sortOrder"); }
            if (request.IsArchived is { } archived && archived != folder.IsArchived)
            {
                folder.IsArchived = archived;
                fields.Add("isArchived");
                archiveEventType = archived
                    ? ContentEventTypes.FolderArchived
                    : ContentEventTypes.FolderRestored;
            }

            // Move handling. Two move axes: ProjectId (cross-project) and
            // ParentFolderId (within the same project tree). Either change
            // requires Edit on the *destination* — Project.Edit for the new
            // project, Folder.Edit for the new parent folder.
            // Caller MUST send projectId + parentFolderId together when moving
            // — the client always has the parent context, and pairing them
            // avoids ambiguous half-moves (e.g. project changes but parent
            // stays pointing into the old project's tree).
            if (request.ProjectId is { } newProjectId && newProjectId != folder.ProjectId)
            {
                var receive = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Project, newProjectId, Actions.Edit, ct);
                if (!receive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

                previousProjectId = folder.ProjectId;
                folder.ProjectId = newProjectId;
                fields.Add("projectId");
                movedParent = true;
                // A cross-project move that leaves parent_folder_id unchanged
                // would dangle (the parent still points into the OLD project).
                // Require the caller to send a fresh parent_folder_id (null
                // for project root) in the same patch.
                if (request.ParentFolderId is null && folder.ParentFolderId is not null)
                {
                    return Results.BadRequest(new
                    {
                        error = "When moving a folder across projects, parentFolderId must be sent " +
                                "(null for project root) so it doesn't dangle into the previous project."
                    });
                }
            }

            if (request.ParentFolderId is not null || (movedParent && request.ParentFolderId is null))
            {
                var newParent = request.ParentFolderId;
                if (newParent != folder.ParentFolderId)
                {
                    if (newParent == folder.Id)
                    {
                        return Results.BadRequest(new { error = "A folder cannot be its own parent." });
                    }
                    if (newParent is { } np)
                    {
                        var parentRow = await db.Folders.AsNoTracking()
                            .Where(f => f.Id == np)
                            .Select(f => new { f.Id, f.ProjectId })
                            .FirstOrDefaultAsync(ct);
                        if (parentRow is null) return Results.BadRequest(new { error = "Parent folder not found." });
                        if (parentRow.ProjectId != folder.ProjectId)
                        {
                            return Results.BadRequest(new
                            {
                                error = "Parent folder belongs to a different project than the destination."
                            });
                        }
                        // Detect a cycle: the new parent must not be a descendant of this folder.
                        var isDescendant = await db.ContentAncestors.AsNoTracking().AnyAsync(ca =>
                            ca.DescendantKind == ContentKinds.Folder
                            && ca.DescendantId == np
                            && ca.AncestorKind == ContentKinds.Folder
                            && ca.AncestorId == folder.Id, ct);
                        if (isDescendant)
                        {
                            return Results.BadRequest(new { error = "Cannot move a folder into one of its own descendants." });
                        }
                        var receiveParent = await authorizer.AuthorizeAsync(
                            http.User, ContentKinds.Folder, np, Actions.Edit, ct);
                        if (!receiveParent.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                    else
                    {
                        // Move to project root requires Project.Edit on the
                        // destination project (which is folder.ProjectId now,
                        // even if a project move just bumped it).
                        var receiveRoot = await authorizer.AuthorizeAsync(
                            http.User, ContentKinds.Project, folder.ProjectId, Actions.Edit, ct);
                        if (!receiveRoot.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                    previousParentFolderId = folder.ParentFolderId;
                    folder.ParentFolderId = newParent;
                    fields.Add("parentFolderId");
                    movedParent = true;
                }
            }

            if (fields.Count == 0)
            {
                return Results.Ok(MapDto(folder));
            }

            folder.UpdatedAtUtc = DateTime.UtcNow;
            folder.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);
            if (movedParent)
            {
                await treeService.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Folder, folder.Id, ct);
            }

            if (movedParent)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.FolderMoved,
                    ContentResourceKinds.Folder,
                    resource: new { id = folder.Id },
                    details: new
                    {
                        previousProjectId,
                        newProjectId = folder.ProjectId,
                        previousParentFolderId,
                        newParentFolderId = folder.ParentFolderId
                    },
                    ct);
            }
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                archiveEventType ?? ContentEventTypes.FolderUpdated,
                ContentResourceKinds.Folder,
                resource: new { id = folder.Id, name = folder.Name },
                details: new { fields },
                ct);

            return Results.Ok(MapDto(folder));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Folder, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (folder is null) return Results.NotFound();
            db.Folders.Remove(folder);
            await db.SaveChangesAsync(ct);
            await treeService.DeleteEntityAsync(db, ContentKinds.Folder, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.FolderDeleted,
                ContentResourceKinds.Folder,
                resource: new { id, name = folder.Name },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Folder, Actions.Delete);

        return app;
    }

    internal static FolderDto MapDto(Folder f) => new(
        f.Id, f.Locator, f.ProjectId, f.ParentFolderId, f.Name, f.Description, f.Icon,
        f.SortOrder, f.IsArchived,
        f.CreatedAtUtc, f.UpdatedAtUtc, f.CreatedBy, f.UpdatedBy);

    public sealed record CreateFolderRequest(
        Guid ProjectId, Guid? ParentFolderId, string Name,
        string? Description, string? Icon, int? SortOrder);

    public sealed record UpdateFolderRequest(
        Guid? ProjectId, Guid? ParentFolderId, string? Name,
        string? Description, string? Icon, int? SortOrder, bool? IsArchived);

    public sealed record FolderDto(
        Guid Id, long Locator, Guid ProjectId, Guid? ParentFolderId,
        string Name, string? Description, string? Icon,
        int SortOrder, bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record FolderPageResponse(List<FolderDto> Items, int TotalCount);

    public sealed record FolderChildrenResponse(
        List<FolderDto> Folders,
        List<ContentDocumentEndpoints.DocumentDto> Documents);
}
