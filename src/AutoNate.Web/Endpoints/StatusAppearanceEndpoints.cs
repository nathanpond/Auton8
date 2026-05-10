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
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Status)
                .Select(x => new StatusAppearanceDto(
                    x.Id,
                    x.Status,
                    x.Color,
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
            var entry = new StatusAppearanceEntry
            {
                Id = Guid.NewGuid(),
                Status = status,
                Color = color,
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

        return app;
    }
    public sealed record StatusAppearanceDto(
        Guid Id,
        string Status,
        string Color,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public sealed record CreateStatusAppearanceRequest(string Status, string Color);

    public sealed record UpdateStatusAppearanceRequest(string? Status, string? Color);
}
