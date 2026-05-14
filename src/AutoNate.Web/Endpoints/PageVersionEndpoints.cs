using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class PageVersionEndpoints
{
    public static IEndpointRouteBuilder MapPageVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/pages/{pageId:guid}/versions")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid pageId,
            int? page,
            int? pageSize,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var query = db.PageVersions.AsNoTracking().Where(v => v.PageId == pageId);
            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(25), 1, 200);
            var items = await query
                .OrderByDescending(v => v.VersionNumber)
                .Skip(pg * ps).Take(ps)
                .Select(v => new PageVersionSummaryDto(
                    v.Id, v.PageId, v.VersionNumber, v.Title, v.Kind, v.Note,
                    v.CreatedAtUtc, v.CreatedBy))
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageVersionListViewed,
                ContentResourceKinds.PageVersion,
                resource: new { pageId },
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps },
                ct);

            return Results.Ok(new PageVersionPageResponse(items, totalCount));
        }).RequirePermission(EntityKinds.Page, Actions.View, "pageId");

        group.MapGet("/{n:int}", async (
            Guid pageId,
            int n,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var version = await db.PageVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.PageId == pageId && v.VersionNumber == n, ct);
            if (version is null) return Results.NotFound();

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageVersionViewed,
                ContentResourceKinds.PageVersion,
                resource: new { pageId, versionNumber = n },
                details: null,
                ct);

            return Results.Ok(new PageVersionDto(
                version.Id, version.PageId, version.VersionNumber,
                version.Title, version.BodyJsonb, version.Kind, version.Note,
                version.CreatedAtUtc, version.CreatedBy));
        }).RequirePermission(EntityKinds.Page, Actions.View, "pageId");

        group.MapPost("/{n:int}/restore", async (
            Guid pageId,
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
                snapshotVersion = await versions.RestorePageAsync(
                    db, pageId, n, request?.Note, actorId, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageVersionRestored,
                ContentResourceKinds.PageVersion,
                resource: new
                {
                    pageId,
                    restoredFromVersion = n,
                    snapshotVersionNumber = snapshotVersion
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Edit, "pageId");

        group.MapDelete("/{n:int}", async (
            Guid pageId,
            int n,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Pruning is a Delete on the page (gates against the same lock as
            // page deletion). The route filter has already verified Delete on
            // the page id, so we can go straight to the operation.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            try
            {
                await versions.DeletePageVersionAsync(db, pageId, n, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageVersionDeleted,
                ContentResourceKinds.PageVersion,
                resource: new { pageId, versionNumber = n },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Delete, "pageId");

        return app;
    }

    public sealed record RestoreRequest(string? Note);

    public sealed record PageVersionSummaryDto(
        Guid Id, Guid PageId, int VersionNumber, string Title, string Kind, string? Note,
        DateTime CreatedAtUtc, Guid CreatedBy);

    public sealed record PageVersionDto(
        Guid Id, Guid PageId, int VersionNumber, string Title, string BodyJsonb,
        string Kind, string? Note, DateTime CreatedAtUtc, Guid CreatedBy);

    public sealed record PageVersionPageResponse(
        List<PageVersionSummaryDto> Items, int TotalCount);
}
