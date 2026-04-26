using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization();

        group.MapGet("/", async (ILocalUserStore store, CancellationToken cancellationToken) =>
        {
            var users = await store.ListAsync(cancellationToken);
            return Results.Ok(users);
        });

        group.MapPost("/", async (
            CreateUserRequest request,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var user = await store.CreateAsync(
                request.Username,
                request.FirstName,
                request.LastName,
                request.Password,
                request.Email,
                cancellationToken);
            return Results.Created($"/api/users/{user.Id}", user);
        }).DisableAntiforgery();

        group.MapPut("/{id:long}", async (
            long id,
            UpdateUserRequest request,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var updated = await store.UpdateAsync(
                id,
                request.Username,
                request.FirstName,
                request.LastName,
                request.Email,
                cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).DisableAntiforgery();

        group.MapPost("/{id:long}/password", async (
            long id,
            ResetPasswordRequest request,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var ok = await store.ResetPasswordAsync(id, request.Password, cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        group.MapDelete("/{id:long}", async (
            long id,
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var ok = await store.DeleteAsync(id, cancellationToken);
            return ok ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery();

        // Supervisor edges. The hierarchy is modeled as entity_edges with
        // edge_kind='supervisor', from = supervisor user, to = supervisee user.
        // GET /supervisors returns the entire hierarchy in one call (used by
        // the admin page); /{id}/supervisor returns one user's supervisor;
        // PUT replaces it (passing null clears).
        group.MapGet("/supervisors", async (
            IDbContextFactory<AutoNateDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pairs = await db.EntityEdges.AsNoTracking()
                .Where(e => e.EdgeKind == EdgeKinds.Supervisor
                         && e.FromKind == EntityKinds.User
                         && e.ToKind == EntityKinds.User)
                .Select(e => new { e.FromId, e.ToId })
                .ToListAsync(ct);

            var result = pairs
                .Select(p => new
                {
                    userId = Guid.TryParse(p.ToId, out var u) ? u : Guid.Empty,
                    supervisorUserId = Guid.TryParse(p.FromId, out var s) ? s : Guid.Empty
                })
                .Where(x => x.userId != Guid.Empty && x.supervisorUserId != Guid.Empty)
                .ToArray();
            return Results.Ok(result);
        });

        group.MapGet("/{userId:guid}/supervisor", async (
            Guid userId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var idString = userId.ToString();
            var supervisorIdString = await db.EntityEdges.AsNoTracking()
                .Where(e => e.EdgeKind == EdgeKinds.Supervisor
                         && e.ToKind == EntityKinds.User
                         && e.ToId == idString
                         && e.FromKind == EntityKinds.User)
                .Select(e => e.FromId)
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                userId,
                supervisorUserId = supervisorIdString is null ? (Guid?)null : Guid.Parse(supervisorIdString)
            });
        });

        group.MapPut("/{userId:guid}/supervisor", async (
            Guid userId,
            SetSupervisorRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IEntityEdgeWriter writer,
            AuthCacheBumper bumper,
            CancellationToken ct) =>
        {
            if (request.SupervisorUserId == userId)
            {
                return Results.BadRequest(new { error = "A user cannot supervise themselves." });
            }

            var actorId = ActorId(http);
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Clear any existing supervisor edge so each user has at most one
            // supervisor — this is the convention the multi-hop selectors expect.
            var idString = userId.ToString();
            var existing = await db.EntityEdges
                .Where(e => e.EdgeKind == EdgeKinds.Supervisor
                         && e.ToKind == EntityKinds.User
                         && e.ToId == idString)
                .ToListAsync(ct);
            if (existing.Count > 0)
            {
                db.EntityEdges.RemoveRange(existing);
            }

            if (request.SupervisorUserId is { } supervisorId)
            {
                writer.AddEdge(
                    db,
                    EdgeKinds.Supervisor,
                    EntityKinds.User, supervisorId.ToString(),
                    EntityKinds.User, idString,
                    actorId,
                    DateTimeOffset.UtcNow);
            }

            await db.SaveChangesAsync(ct);
            await bumper.BumpAsync(ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public sealed record CreateUserRequest(
        string Username,
        string FirstName,
        string LastName,
        string Password,
        string? Email);

    public sealed record UpdateUserRequest(
        string Username,
        string FirstName,
        string LastName,
        string Email);

    public sealed record ResetPasswordRequest(string Password);

    public sealed record SetSupervisorRequest(Guid? SupervisorUserId);
}
