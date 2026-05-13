using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SiteSettings;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AutoNate.Web.Endpoints;

public static class StatusAppearanceEndpoints
{
    public static IEndpointRouteBuilder MapStatusAppearanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/status-appearance").RequireAuthorization();

        group.MapGet("/", async (
            AutoNateDbContext db, IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var entries = await db.StatusAppearanceEntries
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Status)
                .Select(x => new StatusAppearanceDto(
                    x.Id,
                    x.Status,
                    x.Color,
                    x.SortOrder,
                    x.CreatedAtUtc,
                    x.UpdatedAtUtc))
                .ToListAsync(ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.StatusAppearanceListViewed,
                SiteResourceKinds.StatusAppearance,
                resource: null,
                details: new { resultCount = entries.Count },
                ct);
            return Results.Ok(entries);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        group.MapPost("/", async (
            CreateStatusAppearanceRequest request,
            HttpContext http,
            AutoNateDbContext db,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var status = request.Status.Trim();
            var color = request.Color.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(status))
            {
                return Results.BadRequest(new { error = "Status is required." });
            }

            if (string.IsNullOrWhiteSpace(color))
            {
                return Results.BadRequest(new { error = "Color is required." });
            }

            // EF Core translates .ToLower() to SQL LOWER() for Postgres but
            // can't translate .ToLower(CultureInfo) — so the locale-flavor
            // CA rules don't fit this comparison. Status strings are admin-
            // entered ASCII labels; locale-sensitive lowering doesn't matter.
#pragma warning disable CA1304, CA1311
            var exists = await db.StatusAppearanceEntries
                .AnyAsync(x => x.Status.ToLower() == status.ToLower(), ct);
#pragma warning restore CA1304, CA1311
            if (exists)
            {
                return Results.BadRequest(new { error = "That status already exists." });
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            // New rows land at the bottom of the order. SortOrder is per-DB-
            // row, not zero-based — keeps Site_Default at 0 and avoids gaps.
            var maxSort = await db.StatusAppearanceEntries.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;
            var entry = new StatusAppearanceEntry
            {
                Id = Guid.NewGuid(),
                Status = status,
                Color = color,
                SortOrder = maxSort + 1,
                CreatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedAtUtc = now,
                UpdatedBy = actorId
            };

            db.StatusAppearanceEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.StatusAppearanceCreated,
                SiteResourceKinds.StatusAppearance,
                resource: new { id = entry.Id, status = entry.Status, color = entry.Color },
                details: null,
                ct);

            return Results.Created($"/api/admin/status-appearance/{entry.Id}", new StatusAppearanceDto(
                entry.Id,
                entry.Status,
                entry.Color,
                entry.SortOrder,
                entry.CreatedAtUtc,
                entry.UpdatedAtUtc));
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateStatusAppearanceRequest request,
            HttpContext http,
            AutoNateDbContext db,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var entry = await db.StatusAppearanceEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entry is null) return Results.NotFound();

            var nextStatus = request.Status?.Trim() ?? entry.Status;
            var nextColor = request.Color?.Trim().ToLowerInvariant() ?? entry.Color;

            if (string.IsNullOrWhiteSpace(nextStatus))
            {
                return Results.BadRequest(new { error = "Status is required." });
            }

            if (string.IsNullOrWhiteSpace(nextColor))
            {
                return Results.BadRequest(new { error = "Color is required." });
            }

            // Site_Default is the catch-all fallback — its name is referenced
            // by the SPA's color resolver. Color edits are fine; renaming would
            // silently break the fallback for every record without an explicit
            // appearance row.
            if (IsSiteDefault(entry.Status) && !string.Equals(nextStatus, entry.Status, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "Site_Default can't be renamed." });
            }

            // See comment on the matching .ToLower() at the create endpoint.
#pragma warning disable CA1304, CA1311
            var duplicate = await db.StatusAppearanceEntries
                .AnyAsync(x => x.Id != id && x.Status.ToLower() == nextStatus.ToLower(), ct);
#pragma warning restore CA1304, CA1311
            if (duplicate)
            {
                return Results.BadRequest(new { error = "That status already exists." });
            }

            entry.Status = nextStatus;
            entry.Color = nextColor;
            entry.UpdatedAtUtc = DateTime.UtcNow;
            entry.UpdatedBy = http.GetActorId();
            await db.SaveChangesAsync(ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.StatusAppearanceUpdated,
                SiteResourceKinds.StatusAppearance,
                resource: new { id = entry.Id, status = entry.Status, color = entry.Color },
                details: null,
                ct);

            return Results.Ok(new StatusAppearanceDto(
                entry.Id,
                entry.Status,
                entry.Color,
                entry.SortOrder,
                entry.CreatedAtUtc,
                entry.UpdatedAtUtc));
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, AutoNateDbContext db,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var entry = await db.StatusAppearanceEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entry is null) return Results.NotFound();

            // Same reason renames are blocked: the resolver falls back to
            // Site_Default by name. Removing it would leave records without an
            // explicit appearance with the hardcoded #d3d3d3 fallback.
            if (IsSiteDefault(entry.Status))
            {
                return Results.BadRequest(new { error = "Site_Default can't be deleted." });
            }

            db.StatusAppearanceEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.StatusAppearanceDeleted,
                SiteResourceKinds.StatusAppearance,
                resource: new { id, status = entry.Status },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Delete);

        // Bulk reorder. The SPA sends the desired order as a list of ids
        // (Site_Default is excluded — it's pinned client-side and we keep its
        // sort_order = 0 here too). Server reassigns sort_order = 1..N for the
        // ids in the list, leaving any rows not mentioned at their current
        // sort_order (defensive — usually the SPA sends every non-default id).
        group.MapPost("/reorder", async (
            ReorderStatusAppearanceRequest request,
            HttpContext http,
            AutoNateDbContext db,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request.Ids is null) return Results.BadRequest(new { error = "ids is required." });

            var ids = request.Ids.Distinct().ToList();
            var entries = await db.StatusAppearanceEntries
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(ct);
            var byId = entries.ToDictionary(x => x.Id);
            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            var sort = 1;
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var entry)) continue;
                // Skip Site_Default if it slipped into the list — keep it pinned at 0.
                if (IsSiteDefault(entry.Status)) continue;
                entry.SortOrder = sort++;
                entry.UpdatedAtUtc = now;
                entry.UpdatedBy = actorId;
            }
            await db.SaveChangesAsync(ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.StatusAppearanceUpdated,
                SiteResourceKinds.StatusAppearance,
                resource: null,
                details: new { reordered = ids.Count },
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
    }

    private static bool IsSiteDefault(string status) =>
        string.Equals(status?.Trim(), "site_default", StringComparison.OrdinalIgnoreCase);

    public sealed record StatusAppearanceDto(
        Guid Id,
        string Status,
        string Color,
        int SortOrder,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public sealed record CreateStatusAppearanceRequest(string Status, string Color);

    public sealed record UpdateStatusAppearanceRequest(string? Status, string? Color);

    public sealed record ReorderStatusAppearanceRequest(Guid[] Ids);
}
