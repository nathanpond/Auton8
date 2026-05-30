using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Document version history (Phase 2). Mirrors PageVersionEndpoints exactly:
// list / get / restore / delete. Restore captures the current state as a
// `kind='restore'` version before overwriting, so every restore is itself
// reversible. Delete refuses to remove the current row or the only existing
// row, matching the page-version semantics.
public static class DocumentVersionEndpoints
{
    public static IEndpointRouteBuilder MapDocumentVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/documents/{documentId:guid}/versions")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid documentId,
            int? page,
            int? pageSize,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var query = db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == documentId);
            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(25), 1, 200);
            var rows = await query
                .OrderByDescending(v => v.VersionNumber)
                .Skip(pg * ps).Take(ps)
                .Select(v => new
                {
                    v.Id, v.DocumentId, v.VersionNumber, v.Title, v.Kind, v.Note,
                    v.CreatedAtUtc, v.CreatedBy
                })
                .ToListAsync(ct);
            var names = await UserDisplayName.ResolveAsync(
                db, rows.Select(r => r.CreatedBy), ct);
            var items = rows.Select(r => new DocumentVersionSummaryDto(
                r.Id, r.DocumentId, r.VersionNumber, r.Title, r.Kind, r.Note,
                r.CreatedAtUtc, r.CreatedBy,
                names.TryGetValue(r.CreatedBy, out var n) ? n : null)).ToList();

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentVersionListViewed,
                ContentResourceKinds.DocumentVersion,
                resource: new { documentId },
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps },
                ct);

            return Results.Ok(new DocumentVersionPageResponse(items, totalCount));
        }).RequirePermission(EntityKinds.Document, Actions.View, "documentId");

        group.MapGet("/{n:int}", async (
            Guid documentId,
            int n,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var version = await db.DocumentVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.VersionNumber == n, ct);
            if (version is null) return Results.NotFound();
            var names = await UserDisplayName.ResolveAsync(db, new[] { version.CreatedBy }, ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentVersionViewed,
                ContentResourceKinds.DocumentVersion,
                resource: new { documentId, versionNumber = n },
                details: null,
                ct);

            return Results.Ok(new DocumentVersionDto(
                version.Id, version.DocumentId, version.VersionNumber,
                version.Title, version.BodyJsonb, version.Kind, version.Note,
                version.CreatedAtUtc, version.CreatedBy,
                names.TryGetValue(version.CreatedBy, out var n2) ? n2 : null));
        }).RequirePermission(EntityKinds.Document, Actions.View, "documentId");

        group.MapPost("/{n:int}/restore", async (
            Guid documentId,
            int n,
            RestoreRequest? request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            int snapshotVersion;
            try
            {
                snapshotVersion = await versions.RestoreDocumentAsync(
                    db, documentId, n, request?.Note, actorId, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentVersionRestored,
                ContentResourceKinds.DocumentVersion,
                resource: new
                {
                    documentId,
                    restoredFromVersion = n,
                    snapshotVersionNumber = snapshotVersion
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Edit, "documentId");

        group.MapDelete("/{n:int}", async (
            Guid documentId,
            int n,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Pruning a version is a Delete on the document — gates against
            // the same deletions_locked check via the route filter. The
            // service refuses to remove the current version or the only
            // existing version.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            try
            {
                await versions.DeleteDocumentVersionAsync(db, documentId, n, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentVersionDeleted,
                ContentResourceKinds.DocumentVersion,
                resource: new { documentId, versionNumber = n },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Delete, "documentId");

        return app;
    }

    public sealed record RestoreRequest(string? Note);

    public sealed record DocumentVersionSummaryDto(
        Guid Id, Guid DocumentId, int VersionNumber, string Title, string Kind, string? Note,
        DateTime CreatedAtUtc, Guid CreatedBy, string? CreatedByName);

    public sealed record DocumentVersionDto(
        Guid Id, Guid DocumentId, int VersionNumber, string Title, string BodyJsonb,
        string Kind, string? Note, DateTime CreatedAtUtc, Guid CreatedBy, string? CreatedByName);

    public sealed record DocumentVersionPageResponse(
        List<DocumentVersionSummaryDto> Items, int TotalCount);
}
