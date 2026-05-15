using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Note version endpoints. Auth is gated via Page.{View|Edit} after the note
// row is resolved to its pageId — the route doesn't carry pageId, so the
// filter would have nothing to dispatch on; we run AuthorizeAsync inside the
// handler instead. Pruning a note version is NOT subject to deletions_locked
// (notes are exempt per design D10).
public static class NoteVersionEndpoints
{
    public static IEndpointRouteBuilder MapNoteVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/notes/{noteId:guid}/versions")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid noteId,
            int? page,
            int? pageSize,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pageId = await ResolvePageIdAsync(db, noteId, ct);
            if (pageId is null) return Results.NotFound();
            if (!await CheckPageActionAsync(authorizer, http, pageId.Value, Actions.View, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var query = db.NoteVersions.AsNoTracking().Where(v => v.NoteId == noteId);
            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(25), 1, 200);
            var rows = await query
                .OrderByDescending(v => v.VersionNumber)
                .Skip(pg * ps).Take(ps)
                .Select(v => new
                {
                    v.Id, v.NoteId, v.VersionNumber, v.Title, v.NoteKind, v.Kind, v.Note,
                    v.CreatedAtUtc, v.CreatedBy
                })
                .ToListAsync(ct);
            var names = await UserDisplayName.ResolveAsync(
                db, rows.Select(r => r.CreatedBy), ct);
            var items = rows.Select(r => new NoteVersionSummaryDto(
                r.Id, r.NoteId, r.VersionNumber, r.Title, r.NoteKind, r.Kind, r.Note,
                r.CreatedAtUtc, r.CreatedBy,
                names.TryGetValue(r.CreatedBy, out var n) ? n : null)).ToList();

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteVersionListViewed,
                ContentResourceKinds.NoteVersion,
                resource: new { noteId },
                details: new { resultCount = items.Count, totalCount },
                ct);

            return Results.Ok(new NoteVersionPageResponse(items, totalCount));
        });

        group.MapGet("/{n:int}", async (
            Guid noteId,
            int n,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pageId = await ResolvePageIdAsync(db, noteId, ct);
            if (pageId is null) return Results.NotFound();
            if (!await CheckPageActionAsync(authorizer, http, pageId.Value, Actions.View, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var version = await db.NoteVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.NoteId == noteId && v.VersionNumber == n, ct);
            if (version is null) return Results.NotFound();
            var names = await UserDisplayName.ResolveAsync(db, new[] { version.CreatedBy }, ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteVersionViewed,
                ContentResourceKinds.NoteVersion,
                resource: new { noteId, versionNumber = n },
                details: null,
                ct);

            return Results.Ok(new NoteVersionDto(
                version.Id, version.NoteId, version.VersionNumber,
                version.Title, version.NoteKind, version.ContentJsonb,
                version.Kind, version.Note, version.CreatedAtUtc, version.CreatedBy,
                names.TryGetValue(version.CreatedBy, out var n2) ? n2 : null));
        });

        group.MapPost("/{n:int}/restore", async (
            Guid noteId,
            int n,
            RestoreRequest? request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pageId = await ResolvePageIdAsync(db, noteId, ct);
            if (pageId is null) return Results.NotFound();
            if (!await CheckPageActionAsync(authorizer, http, pageId.Value, Actions.Edit, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var actorId = http.GetActorId();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            int snapshotVersion;
            try
            {
                snapshotVersion = await versions.RestoreNoteAsync(
                    db, noteId, n, request?.Note, actorId, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteVersionRestored,
                ContentResourceKinds.NoteVersion,
                resource: new
                {
                    noteId,
                    restoredFromVersion = n,
                    snapshotVersionNumber = snapshotVersion
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery();

        group.MapDelete("/{n:int}", async (
            Guid noteId,
            int n,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pageId = await ResolvePageIdAsync(db, noteId, ct);
            if (pageId is null) return Results.NotFound();
            // Page.Edit is the gate — and note version pruning is NOT subject
            // to the deletions lock (notes are exempt).
            if (!await CheckPageActionAsync(authorizer, http, pageId.Value, Actions.Edit, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                await versions.DeleteNoteVersionAsync(db, noteId, n, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NoteVersionDeleted,
                ContentResourceKinds.NoteVersion,
                resource: new { noteId, versionNumber = n },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery();

        return app;
    }

    private static async Task<Guid?> ResolvePageIdAsync(
        AutoNateDbContext db, Guid noteId, CancellationToken ct)
    {
        return await db.Notes.AsNoTracking()
            .Where(n => n.Id == noteId)
            .Select(n => (Guid?)n.PageId)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<bool> CheckPageActionAsync(
        IContentAuthorizer authorizer, HttpContext http, Guid pageId, string action,
        CancellationToken ct)
    {
        var decision = await authorizer.AuthorizeAsync(
            http.User, ContentKinds.Page, pageId, action, ct);
        return decision.IsAllowed;
    }

    public sealed record RestoreRequest(string? Note);

    public sealed record NoteVersionSummaryDto(
        Guid Id, Guid NoteId, int VersionNumber, string? Title, string NoteKind,
        string Kind, string? Note, DateTime CreatedAtUtc, Guid CreatedBy,
        string? CreatedByName);

    public sealed record NoteVersionDto(
        Guid Id, Guid NoteId, int VersionNumber, string? Title, string NoteKind,
        string ContentJsonb, string Kind, string? Note,
        DateTime CreatedAtUtc, Guid CreatedBy, string? CreatedByName);

    public sealed record NoteVersionPageResponse(
        List<NoteVersionSummaryDto> Items, int TotalCount);
}
