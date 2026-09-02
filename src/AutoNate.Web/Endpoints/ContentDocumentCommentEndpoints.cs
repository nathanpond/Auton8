using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using Npgsql;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Threaded comments for documents (Phase 4).
//
// Comment range markers (commentRangeStart/End) live in the body Y.Doc and
// sync via Hocuspocus; comment metadata lives here in Postgres and is
// fetched via REST. This split matters because (a) the Commenter role
// gets to add comments without editing the body, and per-comment auth is
// far cleaner with a real REST boundary than with Yjs gating; (b) comments
// must be queryable for audit and the future RAG pipeline, which the Y.Doc
// can't deliver without expensive materialization.
//
// Permission model:
//   list       — Document.View
//   create     — Document.Comment (Commenter role + above)
//   reply      — Document.Comment
//   resolve    — Document.Comment
//   reopen     — Document.Comment
//   delete own — Document.Comment (author can prune their own thread)
//   delete any — Document.Edit (Contributor + above can prune any comment)
//
// Numeric `Number` field is the docx-editor-facing id. Client allocates
// via `Math.max(existing) + 1` (default behavior of the editor on add).
// Server enforces (DocumentId, Number) uniqueness; a 409 on collision is
// a real-world race we accept rather than design around.
public static class ContentDocumentCommentEndpoints
{
    public static IEndpointRouteBuilder MapContentDocumentCommentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/documents/{documentId:guid}/comments")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid documentId,
            bool? includeResolved,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var q = db.DocumentComments.AsNoTracking()
                .Where(c => c.DocumentId == documentId);
            // Default: include resolved so the editor sidebar can render
            // them collapsed. Pass `?includeResolved=false` to filter.
            if (includeResolved == false)
            {
                q = q.Where(c => c.ResolvedAtUtc == null);
            }
            var rows = await q
                .OrderBy(c => c.ThreadId)
                .ThenBy(c => c.CreatedAtUtc)
                .ToListAsync(ct);
            var names = await UserDisplayName.ResolveAsync(
                db, rows.Select(r => r.AuthorId).Concat(
                    rows.Where(r => r.ResolvedByUserId is not null)
                        .Select(r => r.ResolvedByUserId!.Value)),
                ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentCommentListViewed,
                ContentResourceKinds.Comment,
                resource: new { documentId },
                details: new { resultCount = rows.Count },
                ct);

            return Results.Ok(new DocumentCommentListResponse(
                rows.Select(r => MapDto(r, names)).ToList()));
        }).RequirePermission(EntityKinds.Document, Actions.View, "documentId");

        group.MapPost("/", async (
            Guid documentId,
            CreateDocumentCommentRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BodyText))
            {
                return Results.BadRequest(new { error = "Comment body is required." });
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var documentExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.Id == documentId, ct);
            if (!documentExists) return Results.NotFound();

            // Number collision check. Client allocates so the editor's body
            // markers reference a stable id immediately on add; a 409
            // signals "try again with a fresh number". Concurrent adds in
            // separate browsers can collide; in practice it's vanishingly
            // rare (the editor uses Math.max(existing)+1).
            var numberTaken = await db.DocumentComments.AsNoTracking()
                .AnyAsync(c => c.DocumentId == documentId && c.Number == request.Number, ct);
            if (numberTaken)
            {
                var nextFree = await db.DocumentComments.AsNoTracking()
                    .Where(c => c.DocumentId == documentId)
                    .Select(c => (int?)c.Number)
                    .MaxAsync(ct) ?? 0;
                return Results.Conflict(new
                {
                    error = "Comment number already exists for this document.",
                    suggestedNumber = nextFree + 1
                });
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            var commentId = Guid.NewGuid();
            var comment = new DocumentComment
            {
                Id = commentId,
                DocumentId = documentId,
                Number = request.Number,
                ParentCommentId = null,
                ThreadId = commentId, // root comment IS its own thread
                AuthorId = actorId,
                BodyText = request.BodyText.Trim(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.DocumentComments.Add(comment);
            var createConflict = await TrySaveOrConflictAsync(db, documentId, ct);
            if (createConflict is not null) return createConflict;

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CommentCreated,
                ContentResourceKinds.Comment,
                resource: new
                {
                    documentId,
                    threadId = comment.ThreadId,
                    commentId = comment.Id,
                    number = comment.Number
                },
                details: null,
                ct);

            var names = await UserDisplayName.ResolveAsync(db, new[] { actorId }, ct);
            return Results.Created(
                $"/api/content/documents/{documentId}/comments/{comment.Id}",
                MapDto(comment, names));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Comment, "documentId");

        group.MapPost("/{commentId:guid}/replies", async (
            Guid documentId,
            Guid commentId,
            CreateDocumentCommentRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BodyText))
            {
                return Results.BadRequest(new { error = "Reply body is required." });
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var parent = await db.DocumentComments.AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.DocumentId == documentId && c.Id == commentId, ct);
            if (parent is null) return Results.NotFound();

            var numberTaken = await db.DocumentComments.AsNoTracking()
                .AnyAsync(c => c.DocumentId == documentId && c.Number == request.Number, ct);
            if (numberTaken)
            {
                var nextFree = await db.DocumentComments.AsNoTracking()
                    .Where(c => c.DocumentId == documentId)
                    .Select(c => (int?)c.Number)
                    .MaxAsync(ct) ?? 0;
                return Results.Conflict(new
                {
                    error = "Comment number already exists for this document.",
                    suggestedNumber = nextFree + 1
                });
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            var reply = new DocumentComment
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Number = request.Number,
                ParentCommentId = commentId,
                // Replies carry the root's ThreadId so the whole conversation
                // is a single indexed range scan.
                ThreadId = parent.ThreadId,
                AuthorId = actorId,
                BodyText = request.BodyText.Trim(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.DocumentComments.Add(reply);
            var replyConflict = await TrySaveOrConflictAsync(db, documentId, ct);
            if (replyConflict is not null) return replyConflict;

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CommentReplied,
                ContentResourceKinds.Comment,
                resource: new
                {
                    documentId,
                    threadId = reply.ThreadId,
                    commentId = reply.Id,
                    parentCommentId = commentId
                },
                details: null,
                ct);

            var names = await UserDisplayName.ResolveAsync(db, new[] { actorId }, ct);
            return Results.Created(
                $"/api/content/documents/{documentId}/comments/{reply.Id}",
                MapDto(reply, names));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Comment, "documentId");

        group.MapPost("/{commentId:guid}/resolve", async (
            Guid documentId,
            Guid commentId,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var comment = await db.DocumentComments
                .FirstOrDefaultAsync(c =>
                    c.DocumentId == documentId && c.Id == commentId, ct);
            if (comment is null) return Results.NotFound();
            if (comment.ResolvedAtUtc is not null) return Results.NoContent();

            // Resolving applies to the WHOLE thread, not just the leaf
            // comment — mark the root and all replies as resolved in one
            // pass so the editor sidebar collapses the thread coherently.
            var now = DateTime.UtcNow;
            var actorId = http.GetActorId();
            await db.DocumentComments
                .Where(c => c.DocumentId == documentId && c.ThreadId == comment.ThreadId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.ResolvedAtUtc, now)
                    .SetProperty(c => c.ResolvedByUserId, actorId)
                    .SetProperty(c => c.UpdatedAtUtc, now), ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CommentResolved,
                ContentResourceKinds.Comment,
                resource: new
                {
                    documentId,
                    threadId = comment.ThreadId,
                    commentId = comment.Id
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Comment, "documentId");

        group.MapPost("/{commentId:guid}/reopen", async (
            Guid documentId,
            Guid commentId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var comment = await db.DocumentComments
                .FirstOrDefaultAsync(c =>
                    c.DocumentId == documentId && c.Id == commentId, ct);
            if (comment is null) return Results.NotFound();
            if (comment.ResolvedAtUtc is null) return Results.NoContent();

            var now = DateTime.UtcNow;
            await db.DocumentComments
                .Where(c => c.DocumentId == documentId && c.ThreadId == comment.ThreadId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.ResolvedAtUtc, (DateTime?)null)
                    .SetProperty(c => c.ResolvedByUserId, (Guid?)null)
                    .SetProperty(c => c.UpdatedAtUtc, now), ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CommentReopened,
                ContentResourceKinds.Comment,
                resource: new
                {
                    documentId,
                    threadId = comment.ThreadId,
                    commentId = comment.Id
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Comment, "documentId");

        // Delete semantics: authors can prune their own comment (and only
        // their leaf reply, never another author's thread root unless they
        // have Edit on the document). Document.Edit + above can prune any
        // comment.
        group.MapDelete("/{commentId:guid}", async (
            Guid documentId,
            Guid commentId,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var comment = await db.DocumentComments
                .FirstOrDefaultAsync(c =>
                    c.DocumentId == documentId && c.Id == commentId, ct);
            if (comment is null) return Results.NotFound();

            var actorId = http.GetActorId();
            var isAuthor = comment.AuthorId == actorId;
            if (!isAuthor)
            {
                // Non-authors need Edit on the document. Comment-level
                // authz already checked by the route filter (Document.Comment
                // for visibility); this layered check upgrades the gate
                // for delete-others.
                var editDecision = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Document, documentId, Actions.Edit, ct);
                if (!editDecision.IsAllowed)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }

            db.DocumentComments.Remove(comment);
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CommentDeleted,
                ContentResourceKinds.Comment,
                resource: new
                {
                    documentId,
                    threadId = comment.ThreadId,
                    commentId = comment.Id
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Comment, "documentId")
          .AuthorizedInHandler(
              "Document.Comment route filter gates visibility; the handler " +
              "additionally requires Document.Edit when the caller is not " +
              "the author of the target comment.");

        return app;
    }

    private static DocumentCommentDto MapDto(
        DocumentComment c, IReadOnlyDictionary<Guid, string> names) =>
        new(
            c.Id,
            c.DocumentId,
            c.Number,
            c.ParentCommentId,
            c.ThreadId,
            c.AuthorId,
            names.TryGetValue(c.AuthorId, out var an) ? an : null,
            c.BodyText,
            c.ResolvedAtUtc,
            c.ResolvedByUserId,
            c.ResolvedByUserId is { } rid && names.TryGetValue(rid, out var rn) ? rn : null,
            c.CreatedAtUtc,
            c.UpdatedAtUtc);

    public sealed record CreateDocumentCommentRequest(int Number, string BodyText);

    public sealed record DocumentCommentDto(
        Guid Id,
        Guid DocumentId,
        int Number,
        Guid? ParentCommentId,
        Guid ThreadId,
        Guid AuthorId,
        string? AuthorName,
        string BodyText,
        DateTime? ResolvedAtUtc,
        Guid? ResolvedByUserId,
        string? ResolvedByUserName,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public sealed record DocumentCommentListResponse(List<DocumentCommentDto> Items);

    // The (document_id, number) collision check above is a TOCTOU: the unique
    // index is real, so two browsers picking the same number can both pass the
    // SELECT and one loses at INSERT. The code already called that out as "a
    // real-world race we accept" — but with nothing catching DbUpdateException
    // the loser got an unhandled 500 instead of the 409 the handler promises
    // three lines earlier, and the client's retry logic keys off that 409
    // (archived-186).
    private static async Task<IResult?> TrySaveOrConflictAsync(
        AutoNateDbContext db, Guid documentId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Same answer the pre-check gives, computed the same way, so a
            // caller cannot tell which path produced it.
            var nextFree = await db.DocumentComments.AsNoTracking()
                .Where(c => c.DocumentId == documentId)
                .Select(c => (int?)c.Number)
                .MaxAsync(ct) ?? 0;
            return Results.Conflict(new
            {
                error = "Comment number already exists for this document.",
                suggestedNumber = nextFree + 1
            });
        }
    }

}
