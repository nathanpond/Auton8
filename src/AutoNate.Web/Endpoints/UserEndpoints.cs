using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization();

        group.MapGet("/", async (
            ILocalUserStore store, IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var users = await store.ListAsync(cancellationToken);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.UserListViewed,
                IamResourceKinds.User,
                resource: null,
                details: new { resultCount = users.Count },
                cancellationToken);
            return Results.Ok(users);
        }).RequireKindPermission(EntityKinds.User, Actions.View);

        // Authenticated-only minimal user directory. Returns the same LocalUser
        // shape as GET / above but with admin-only fields (email, idp key,
        // last login, lock state) blanked. Powers collab features (Yjs cursor
        // names, comment authors, project-member pickers) that must work for
        // any project member, not just admins with User.View. Intentionally
        // not audited — called on every editor mount, would flood the log.
        group.MapGet("/directory", async (
            ILocalUserStore store,
            CancellationToken cancellationToken) =>
        {
            var users = await store.ListAsync(cancellationToken);
            return Results.Ok(users.Select(u => u with
            {
                Email = string.Empty,
                IdpKey = string.Empty,
                LastLoginDate = null,
                FailedLoginAttempts = 0,
                IsLocked = false,
                LockedAtUtc = null
            }));
        }).OpenToAuthenticated(
            "Authenticated-only minimal user directory used by collab UI " +
            "(Yjs cursor names, comment authors, member pickers); admin-" +
            "only fields are blanked before the response.");

        // Paged variant of GET /api/users for tables that fetch one screen at
        // a time. Returns { items, totalCount } and supports search, status
        // filter, and sort by username/fullName/lastName/lastLogin/status.
        // pageSize=0 is a "count probe" — items come back empty, totalCount is
        // populated; clients use this to decide between client- and server-
        // side modes without paying to download the full list.
        group.MapGet("/page", async (
            int? page,
            int? pageSize,
            string? q,
            string? sort,
            string? sortDir,
            string? status,
            ILocalUserStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var request = new ListLocalUsersRequest(
                Page: page ?? 0,
                PageSize: pageSize ?? 25,
                Search: q,
                SortBy: sort,
                SortDir: sortDir,
                Status: status);
            var result = await store.ListPagedAsync(request, cancellationToken);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.UserListViewed,
                IamResourceKinds.User,
                resource: null,
                details: new
                {
                    resultCount = result.Items.Count,
                    totalCount = result.TotalCount,
                    page = request.Page,
                    pageSize = request.PageSize,
                    search = request.Search,
                    status = request.Status
                },
                cancellationToken);
            return Results.Ok(result);
        }).RequireKindPermission(EntityKinds.User, Actions.View);

        group.MapPost("/", async (
            CreateUserRequest request,
            ILocalUserStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var user = await store.CreateAsync(
                request.Username,
                request.FirstName,
                request.LastName,
                request.Password,
                request.Email,
                cancellationToken);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.UserCreated,
                IamResourceKinds.User,
                resource: new { id = user.Id, userId = user.UserId, username = user.Username },
                details: null,
                cancellationToken);
            return Results.Created($"/api/users/{user.Id}", user);
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.User, Actions.Create);

        // Route id is the local users table pkey (long) and the authorization
        // entity is keyed by the user's Guid, so we gate at the kind level
        // (matches the existing /unlock convention below). Per-user grants on
        // these mutations would require translating long → Guid in a custom
        // filter, which isn't worth the complexity for admin-only routes.
        group.MapPut("/{id:long}", async (
            long id,
            UpdateUserRequest request,
            ILocalUserStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var updated = await store.UpdateAsync(
                id,
                request.Username,
                request.FirstName,
                request.LastName,
                request.Email,
                cancellationToken);
            if (updated is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.UserUpdated,
                IamResourceKinds.User,
                resource: new { id = updated.Id, userId = updated.UserId, username = updated.Username },
                details: null,
                cancellationToken);
            return Results.Ok(updated);
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.User, Actions.Edit);

        group.MapPost("/{id:long}/password", async (
            long id,
            ResetPasswordRequest request,
            ILocalUserStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var ok = await store.ResetPasswordAsync(id, request.Password, cancellationToken);
            if (!ok) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.UserPasswordReset,
                IamResourceKinds.User,
                resource: new { id },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.User, Actions.Edit);

        group.MapPost("/{id:long}/unlock", async (
            long id,
            ILocalUserStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var updated = await store.SetLockedAsync(id, isLocked: false, cancellationToken);
            if (updated is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.AccountUnlocked,
                AuthEventTopic.ResourceKind,
                resource: new { id = updated.Id, userId = updated.UserId, username = updated.Username },
                details: null,
                cancellationToken);
            return Results.Ok(updated);
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.User, Actions.Unlock);

        group.MapDelete("/{id:long}", async (
            long id,
            ILocalUserStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            // Snapshot the username before delete so the audit log shows
            // "alice" instead of a bare numeric id.
            var snapshot = await store.GetByIdAsync(id, cancellationToken);
            var ok = await store.DeleteAsync(id, cancellationToken);
            if (!ok) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.UserDeleted,
                IamResourceKinds.User,
                resource: new { id, username = snapshot?.Username, userId = snapshot?.UserId },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.User, Actions.Delete);

        // Supervisor edges. The hierarchy is modeled as entity_edges with
        // edge_kind='supervisor', from = supervisor user, to = supervisee user.
        // GET /supervisors returns the entire hierarchy in one call (used by
        // the admin page); /{id}/supervisor returns one user's supervisor;
        // PUT replaces it (passing null clears).
        group.MapGet("/supervisors", async (
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
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
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.SupervisorsListViewed,
                IamResourceKinds.Supervisor,
                resource: null,
                details: new { resultCount = result.Length },
                ct);
            return Results.Ok(result);
        }).RequireKindPermission(EntityKinds.User, Actions.View);

        group.MapGet("/{userId:guid}/supervisor", async (
            Guid userId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
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

            var supervisorUserId = supervisorIdString is null ? (Guid?)null : Guid.Parse(supervisorIdString);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.SupervisorViewed,
                IamResourceKinds.Supervisor,
                resource: new { userId, supervisorUserId },
                details: null,
                ct);
            return Results.Ok(new { userId, supervisorUserId });
        }).RequirePermission(EntityKinds.User, Actions.View, "userId");

        group.MapPut("/{userId:guid}/supervisor", async (
            Guid userId,
            SetSupervisorRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IEntityEdgeWriter writer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (request.SupervisorUserId == userId)
            {
                return Results.BadRequest(new { error = "A user cannot supervise themselves." });
            }

            var actorId = http.GetActorId();
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
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                request.SupervisorUserId is null
                    ? IamEventTypes.SupervisorCleared
                    : IamEventTypes.SupervisorSet,
                IamResourceKinds.Supervisor,
                resource: new { userId, supervisorUserId = request.SupervisorUserId },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.User, Actions.Edit, "userId");

        return app;
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
