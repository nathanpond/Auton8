using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;

namespace AutoNate.Web.Endpoints;

public sealed record WatchedRecordDto(
    Guid Id,
    Guid RecordTypeId,
    string Key,
    string Name,
    string? Status,
    DateOnly? DueDate,
    string? Description,
    Guid[] AssigneeIds,
    bool IsArchived,
    DateTimeOffset WatchedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WatchedRecordsPageDto(
    WatchedRecordDto[] Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record WatchStatusDto(bool IsWatching);

public static class RecordWatchEndpoints
{
    public static IEndpointRouteBuilder MapRecordWatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/records").RequireAuthorization();

        // List the records the current user is watching, filtered by the
        // actor's record-visibility grants so we don't leak rows for records
        // they can no longer see.
        group.MapGet("/watched-by-me", async (
            int? page,
            int? pageSize,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            var resolvedPage = Math.Max(0, page ?? 0);
            var resolvedPageSize = Math.Clamp(pageSize ?? 25, 1, 200);

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var watchedRecordIds = db.RecordWatches
                .AsNoTracking()
                .Where(w => w.UserId == actorId)
                .Select(w => w.RecordId);

            IQueryable<RecordEntity> baseQuery = db.Records.AsNoTracking()
                .Where(r => watchedRecordIds.Contains(r.Id));

            var visible = await authorizer.FilterQueryAsync(
                db, http.User, EntityKinds.Record, Actions.View, baseQuery, cancellationToken);

            var totalCount = await visible.CountAsync(cancellationToken);

            // Order by when the user started watching (newest first).
            var rows = await (
                from r in visible
                join w in db.RecordWatches.AsNoTracking()
                    on new { RecordId = r.Id, UserId = actorId }
                    equals new { w.RecordId, w.UserId }
                orderby w.CreatedAtUtc descending
                select new { Record = r, WatchedAtUtc = w.CreatedAtUtc })
                .Skip(resolvedPage * resolvedPageSize)
                .Take(resolvedPageSize)
                .ToListAsync(cancellationToken);

            var items = rows.Select(row => ToDto(row.Record, row.WatchedAtUtc)).ToArray();

            await auditPublisher.PublishAsync(
                DaprRecordEventPublisher.TopicName,
                RecordEventTypes.ListViewed,
                RecordResourceKinds.Record,
                resource: null,
                details: new
                {
                    scope = "watched-by-me",
                    page = resolvedPage,
                    pageSize = resolvedPageSize,
                    resultCount = items.Length,
                    totalCount
                },
                cancellationToken);

            return Results.Ok(new WatchedRecordsPageDto(items, totalCount, resolvedPage, resolvedPageSize));
        });

        // Whether the current user is watching the given record.
        group.MapGet("/{id:guid}/watch", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var isWatching = await db.RecordWatches.AsNoTracking()
                .AnyAsync(w => w.UserId == actorId && w.RecordId == id, cancellationToken);

            return Results.Ok(new WatchStatusDto(isWatching));
        }).RequirePermission(EntityKinds.Record, Actions.View);

        // Watch a record. Idempotent — re-watching a record returns 200.
        group.MapPost("/{id:guid}/watch", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            var record = await store.GetAsync(id, cancellationToken);
            if (record is null) return Results.NotFound();

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.RecordWatches
                .SingleOrDefaultAsync(w => w.UserId == actorId && w.RecordId == id, cancellationToken);
            if (existing is null)
            {
                db.RecordWatches.Add(new RecordWatch
                {
                    UserId = actorId,
                    RecordId = id,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new WatchStatusDto(true));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.View);

        // Unwatch a record. Idempotent — unwatching a record you don't watch
        // is a 200.
        group.MapDelete("/{id:guid}/watch", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.RecordWatches
                .SingleOrDefaultAsync(w => w.UserId == actorId && w.RecordId == id, cancellationToken);
            if (existing is not null)
            {
                db.RecordWatches.Remove(existing);
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new WatchStatusDto(false));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.View);

        return app;
    }
    private static WatchedRecordDto ToDto(RecordEntity record, DateTime watchedAtUtc)
    {
        return new WatchedRecordDto(
            record.Id,
            record.RecordTypeId,
            record.Key,
            record.Name,
            record.Status,
            record.DueDate,
            ReadDescription(record.Values),
            record.AssigneeIds ?? Array.Empty<Guid>(),
            record.IsArchived,
            new DateTimeOffset(DateTime.SpecifyKind(watchedAtUtc, DateTimeKind.Utc), TimeSpan.Zero),
            new DateTimeOffset(DateTime.SpecifyKind(record.UpdatedAtUtc, DateTimeKind.Utc), TimeSpan.Zero));
    }

    private static string? ReadDescription(string valuesJson)
    {
        if (string.IsNullOrWhiteSpace(valuesJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(valuesJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "description", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var raw = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) return null;
                    return raw.Trim();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        return null;
    }
}
