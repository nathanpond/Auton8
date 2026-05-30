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

        // Clone-from-template (Phase 6). Reads the template (any
        // kind='template' Document the caller can View), creates a fresh
        // kind='document' Document with the template's body + a fresh
        // copy of every binding. Comments are NOT cloned — they're
        // tied to the template's review process, not generic content.
        // Body placeholder text gets rewritten (`{{binding:OLD}}` →
        // `{{binding:NEW}}`) so the cloned bindings line up with the
        // copied body. The new doc's body_jsonb gets the rewritten
        // text; the Y.Doc seeds from that mirror on first connect (the
        // sidecar's `trySeedFromBodyMirror` handles this — works for
        // documents the same way it works for pages).
        //
        // Permission: View on the template (route filter) + Edit on
        // the destination folder (or Project.Edit at root) — same D9
        // composition the create path uses.
        group.MapPost("/from-template/{templateId:guid}", async (
            Guid templateId,
            CloneFromTemplateRequest request,
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
                return Results.BadRequest(new { error = "Title is required." });
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var template = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == templateId, ct);
            if (template is null) return Results.NotFound();
            if (template.Kind != DocumentKinds.Template)
            {
                return Results.BadRequest(new { error = "Source is not a template." });
            }
            if (template.ProjectId != request.ProjectId)
            {
                return Results.BadRequest(new
                {
                    error = "Template belongs to a different project than the destination. " +
                            "Templates can't be cloned across projects in v1."
                });
            }

            // D9 gate on the destination — same shape the create path
            // uses for body documents.
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

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            var newDocId = Guid.NewGuid();

            // Clone bindings first — we need the old-id → new-id map
            // to rewrite the body text BEFORE persisting the new doc.
            var sourceBindings = await db.DocumentBindings.AsNoTracking()
                .Where(b => b.DocumentId == templateId)
                .ToListAsync(ct);
            var bindingIdMap = new Dictionary<Guid, Guid>(sourceBindings.Count);
            foreach (var sb in sourceBindings)
            {
                bindingIdMap[sb.Id] = Guid.NewGuid();
            }

            // Rewrite `{{binding:OLD}}` → `{{binding:NEW}}` in the body.
            // String replace is safe because UUIDs don't collide and
            // placeholder syntax is well-defined.
            var newBody = template.BodyJsonb;
            foreach (var (oldId, newId) in bindingIdMap)
            {
                newBody = newBody.Replace(
                    $"{{{{binding:{oldId}}}}}",
                    $"{{{{binding:{newId}}}}}",
                    StringComparison.OrdinalIgnoreCase);
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var newDoc = new Document
            {
                Id = newDocId,
                ProjectId = request.ProjectId,
                FolderId = request.FolderId,
                Kind = DocumentKinds.Document,
                TemplateId = templateId,
                Title = request.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description!.Trim(),
                BodyJsonb = newBody,
                CurrentVersionNumber = 1,
                SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actorId, UpdatedBy = actorId
            };
            db.Documents.Add(newDoc);

            // Insert bindings with fresh ids, fresh document_id, no
            // resolved values — the new doc will resolve on first open
            // or on explicit refresh. This is intentional: the template
            // may have a stale cached value, and per-row authorization
            // can return different results for different callers.
            foreach (var sb in sourceBindings)
            {
                db.DocumentBindings.Add(new DocumentBinding
                {
                    Id = bindingIdMap[sb.Id],
                    DocumentId = newDocId,
                    Kind = sb.Kind,
                    ConfigJsonb = sb.ConfigJsonb,
                    LastResolvedValueJsonb = null,
                    LastResolvedAtUtc = null,
                    LastResolvedByUserId = null,
                    Label = sb.Label,
                    CreatedAtUtc = now, UpdatedAtUtc = now,
                    CreatedBy = actorId, UpdatedBy = actorId
                });
            }

            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, newDocId, ct);

            var initialVersion = await versions.SnapshotDocumentBeforeChangeAsync(
                db, newDocId, newDoc.Title, newDoc.BodyJsonb,
                ContentVersionKinds.Manual, $"Cloned from template {templateId}",
                actorId, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentCreated,
                ContentResourceKinds.Document,
                resource: new
                {
                    id = newDoc.Id,
                    projectId = newDoc.ProjectId,
                    folderId = newDoc.FolderId,
                    title = newDoc.Title
                },
                details: new
                {
                    kind = newDoc.Kind,
                    templateId,
                    bindingsCloned = sourceBindings.Count,
                    source = "template-clone"
                },
                ct);
            if (initialVersion is { } v)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.DocumentVersionCreated,
                    ContentResourceKinds.DocumentVersion,
                    resource: new { documentId = newDoc.Id, versionNumber = v, kind = ContentVersionKinds.Manual },
                    details: null,
                    ct);
            }

            return Results.Created(
                $"/api/content/documents/{newDoc.Id}",
                MapDto(newDoc));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.View, "templateId")
          .AuthorizedInHandler(
              "View on the template gates discovery (route filter). The handler " +
              "additionally requires Edit on the destination folder, or " +
              "Project.Edit at the root, per the D9 composition.");

        // ── Phase 7: DOCX / DOTX import ────────────────────────────────────
        //
        // Three endpoints make the import path work end-to-end:
        //   POST   /import                      — upload .docx/.dotx, create
        //                                          Document row, stash bytes
        //   GET    /{id}/import-buffer          — fetch stashed bytes for the
        //                                          editor's first mount
        //   DELETE /{id}/import-buffer          — discard the stash once the
        //                                          editor's first autosave has
        //                                          materialized body_jsonb
        //
        // Why a transient stash instead of putting bytes in the DB or in
        // body_jsonb: docx-editor parses OOXML into its own ProseMirror state
        // on mount via the `documentBuffer` prop, then drives normal Yjs
        // autosave. body_jsonb only becomes meaningful after that first
        // snapshot. Storing the raw .docx in body_jsonb (or even keeping a
        // permanent copy) would double the storage footprint without
        // adding round-trip fidelity — re-export goes through docx-editor's
        // `save()`, not the original bytes.

        // Upload endpoint. Multipart with one `file` part + `projectId`,
        // optional `folderId` + `title`. The Document.Kind discriminator
        // is auto-derived from the file extension: `.docx` → 'document',
        // `.dotx` → 'template'. Mirrors the create-document permission
        // model (Folder.Edit nested, Project.Edit at root) since this is
        // effectively a create-document POST with a binary side channel.
        group.MapPost("/import", async (
            HttpRequest req,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IContentVersionService versions,
            IDocumentImportStorage importStore,
            Microsoft.Extensions.Options.IOptions<DocumentImportOptions> importOpts,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart/form-data required." });
            }
            var form = await req.ReadFormAsync(ct);
            if (form.Files.Count == 0)
            {
                return Results.BadRequest(new { error = "No file provided." });
            }
            var file = form.Files[0];
            var options = importOpts.Value;
            if (file.Length <= 0)
            {
                return Results.BadRequest(new { error = "Uploaded file is empty." });
            }
            if (file.Length > options.MaxBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            // Extension dispatch: .docx → document, .dotx → template. We
            // refuse anything else outright; legacy .doc / .dot (CFB,
            // pre-OOXML) is not supported by docx-editor.
            var fileName = file.FileName ?? string.Empty;
            string documentKind;
            if (fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                documentKind = DocumentKinds.Document;
            }
            else if (fileName.EndsWith(".dotx", StringComparison.OrdinalIgnoreCase))
            {
                documentKind = DocumentKinds.Template;
            }
            else
            {
                return Results.BadRequest(new
                {
                    error = "Only .docx and .dotx uploads are supported."
                });
            }

            // Read once into memory for the sniff + the stash write. 25 MB
            // ceiling is enforced above so the buffer is bounded.
            await using var uploadStream = file.OpenReadStream();
            await using var ms = new MemoryStream();
            await uploadStream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            // OOXML containers are PKZIP — the sniffer returns
            // "application/zip" for both .docx and .dotx since the
            // container format is identical. We don't go further than
            // confirming the family because we hand the bytes to
            // docx-editor's parser; it owns OOXML-internal validation.
            var sniffed = ContentTypeSniffer.Sniff(bytes);
            if (sniffed != "application/zip")
            {
                return Results.BadRequest(new
                {
                    error = "Uploaded file is not a valid OOXML document " +
                            "(.docx / .dotx requires the OOXML / ZIP container format)."
                });
            }

            // Parse the rest of the form. Title falls back to the file's
            // base name without extension so a paste-as-new-document flow
            // works without the SPA filling it in.
            if (!Guid.TryParse(form["projectId"].ToString(), out var projectId))
            {
                return Results.BadRequest(new { error = "Missing or invalid projectId." });
            }
            Guid? folderId = null;
            if (Guid.TryParse(form["folderId"].ToString(), out var parsedFolder))
            {
                folderId = parsedFolder;
            }
            var requestedTitle = form["title"].ToString();
            if (string.IsNullOrWhiteSpace(requestedTitle))
            {
                requestedTitle = Path.GetFileNameWithoutExtension(fileName);
            }
            if (string.IsNullOrWhiteSpace(requestedTitle))
            {
                requestedTitle = documentKind == DocumentKinds.Template
                    ? "Imported template"
                    : "Imported document";
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var projectExists = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == projectId, ct);
            if (!projectExists) return Results.BadRequest(new { error = "Project not found." });

            AuthDecision decision;
            if (folderId is { } fid)
            {
                var folder = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == fid)
                    .Select(f => new { f.Id, f.ProjectId })
                    .FirstOrDefaultAsync(ct);
                if (folder is null) return Results.BadRequest(new { error = "Folder not found." });
                if (folder.ProjectId != projectId)
                {
                    return Results.BadRequest(new { error = "Folder belongs to a different project." });
                }
                decision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Folder, fid, Actions.Edit, ct);
            }
            else
            {
                decision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Project, projectId, Actions.Edit, ct);
            }
            if (!decision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            var documentId = Guid.NewGuid();

            // Stash bytes first. If the DB insert fails afterwards we'll
            // clean up via the catch block so we don't orphan a file
            // referenced by no Document row.
            using (var writeStream = new MemoryStream(bytes, writable: false))
            {
                await importStore.WriteAsync(documentId, writeStream, ct);
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var doc = new Document
            {
                Id = documentId,
                ProjectId = projectId,
                FolderId = folderId,
                Kind = documentKind,
                Title = requestedTitle.Trim(),
                // body_jsonb stays empty until docx-editor parses the
                // stash and the first autosave commits the JSON. An
                // empty `{}` object is a valid ProseMirror placeholder
                // — the editor route checks for `?import=1` before
                // hydrating from body_jsonb.
                BodyJsonb = "{}",
                CurrentVersionNumber = 1,
                SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actorId, UpdatedBy = actorId
            };
            db.Documents.Add(doc);
            try
            {
                await db.SaveChangesAsync(ct);
                await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, doc.Id, ct);
                var initialVersion = await versions.SnapshotDocumentBeforeChangeAsync(
                    db, doc.Id, doc.Title, doc.BodyJsonb,
                    ContentVersionKinds.Manual,
                    $"Imported from {Path.GetFileName(fileName)}",
                    actorId, now, ct);
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
                    details: new
                    {
                        kind = doc.Kind,
                        source = "import",
                        sourceFileName = Path.GetFileName(fileName),
                        sourceByteSize = bytes.LongLength
                    },
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
            }
            catch
            {
                await importStore.DeleteAsync(documentId, ct);
                throw;
            }

            return Results.Created($"/api/content/documents/{doc.Id}", MapDto(doc));
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Project / folder Edit gate same as create. Extension dispatch " +
              "auto-routes .docx → document, .dotx → template; OOXML container " +
              "is enforced via magic-byte sniff.");

        // Fetch the stashed bytes for the editor's first mount. Streams
        // directly with a fixed Content-Type — the file is always treated
        // as the OOXML wordprocessingml container regardless of whether
        // it was uploaded as .docx or .dotx (docx-editor parses both
        // identically; the kind discriminator lives on the Document row).
        group.MapGet("/{id:guid}/import-buffer", async (
            Guid id,
            IDocumentImportStorage importStore,
            CancellationToken ct) =>
        {
            if (!importStore.Exists(id))
            {
                return Results.NotFound(new { error = "No pending import for this document." });
            }
            var stream = await importStore.ReadAsync(id, ct);
            return Results.Stream(stream,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }).RequirePermission(EntityKinds.Document, Actions.View);

        // Discard the stash. Called by the editor route after the first
        // successful Yjs snapshot lands so the JSON mirror takes over as
        // source of truth. Best-effort: 204 always (we don't surface
        // filesystem errors to the client because the DB row is the
        // durable artifact; orphaned bytes are reaped by the optional
        // sweep job).
        group.MapDelete("/{id:guid}/import-buffer", async (
            Guid id,
            IDocumentImportStorage importStore,
            CancellationToken ct) =>
        {
            await importStore.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(EntityKinds.Document, Actions.Edit);

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

    public sealed record CloneFromTemplateRequest(
        Guid ProjectId, Guid? FolderId,
        string Title, string? Description);

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
