using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Admin surface for the projection framework. Gated through SiteConfig:Edit
// so anyone with platform-config rights can see and operate the projections;
// finer per-projection gating would need a new EntityKinds.Projection
// registration and is a follow-up if multiple admin roles emerge.
//
// The endpoints are deliberately small — health is a read, the action
// endpoints flip an in-memory pause flag or invoke BackfillRunner / reset
// the watermark. Auditing is via the standard endpoint filter chain.
public static class AdminProjectionsEndpoints
{
    public static IEndpointRouteBuilder MapAdminProjectionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/projections").RequireAuthorization();

        group.MapGet("/", (
                IProjectionRegistry registry,
                IProjectionHealthService health) =>
            {
                var snaps = health.Snapshot(registry.Projections);
                return Results.Ok(snaps);
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapGet("/{name}", (
                string name,
                IProjectionRegistry registry,
                IProjectionHealthService health) =>
            {
                var p = registry.TryGet(name);
                if (p is null) return Results.NotFound(new { error = $"Projection '{name}' is not registered." });
                return Results.Ok(health.Snapshot(p));
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapPost("/{name}/pause", (
                string name,
                IProjectionRegistry registry,
                IProjectionHealthService health) =>
            {
                if (registry.TryGet(name) is null)
                {
                    return Results.NotFound(new { error = $"Projection '{name}' is not registered." });
                }
                health.Pause(name);
                return Results.Ok(new ProjectionActionResult(true, $"Projection '{name}' paused."));
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapPost("/{name}/resume", (
                string name,
                IProjectionRegistry registry,
                IProjectionHealthService health) =>
            {
                if (registry.TryGet(name) is null)
                {
                    return Results.NotFound(new { error = $"Projection '{name}' is not registered." });
                }
                health.Resume(name);
                return Results.Ok(new ProjectionActionResult(true, $"Projection '{name}' resumed."));
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapPost("/{name}/rebuild", async (
                string name,
                BackfillRunner runner,
                IProjectionRegistry registry,
                CancellationToken ct) =>
            {
                if (registry.TryGet(name) is null)
                {
                    return Results.NotFound(new { error = $"Projection '{name}' is not registered." });
                }
                try
                {
                    var rows = await runner.RunAsync(name, cancellationToken: ct);
                    return Results.Ok(new ProjectionActionResult(true,
                        $"Backfill of projection '{name}' wrote {rows} rows."));
                }
                catch (InvalidOperationException ex)
                {
                    // No backfill source registered for this projection.
                    return Results.BadRequest(new ProjectionActionResult(false, ex.Message));
                }
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapPost("/feeds/{feedName}/reset-watermark", async (
                string feedName,
                IDbContextFactory<AutoNateDbContext> dbFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM projection_watermarks WHERE feed_name = {feedName}",
                    ct);
                return Results.Ok(new ProjectionActionResult(
                    Ok: deleted > 0,
                    Message: deleted > 0
                        ? $"Reset watermark for feed '{feedName}'."
                        : $"No watermark row found for feed '{feedName}'."));
            })
            .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
    }
}
