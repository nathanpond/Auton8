using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace AutoNate.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();

        // Hot endpoint: every authenticated SPA navigation hits /api/auth/me
        // at least once on mount (useMe consumed by ProtectedRoute, NavMenu,
        // useNotifications, UserProfile) plus React Query refetches on
        // window focus. The previous implementation issued 2 + 1 + N + 1
        // queries (groups twice, direct assignments, one per group, full
        // role table); this one does at most 3 (groups, batched assignments,
        // batched roles by id) and coalesces the audit event per user.
        group.MapGet("/me", async (
            HttpContext context,
            IRoleStore roleStore,
            IRoleAssignmentStore assignments,
            IGroupStore groupStore,
            IAuditEventPublisher auditPublisher,
            ViewEventCoalescer coalescer,
            CancellationToken ct) =>
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { authenticated = false });
            }

            var userIdRaw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId = Guid.TryParse(userIdRaw, out var parsed) ? parsed : Guid.Empty;

            // Fetch the user's groups exactly once and reuse for both the
            // response payload (display id+name) and the assignment lookup
            // (id only).
            var memberships = userId == Guid.Empty
                ? Array.Empty<Models.Authorization.Group>()
                : (await groupStore.ListGroupsForUserAsync(userId, ct)).ToArray();
            var groups = memberships
                .Select(g => (object)new { id = g.Id, name = g.Name })
                .ToArray();
            var groupPrincipalIds = memberships
                .Select(g => g.Id.ToString())
                .ToArray();

            var directAssignments = userId == Guid.Empty
                ? Array.Empty<Models.Authorization.RoleAssignment>()
                : (await assignments.ListForPrincipalAsync(EntityKinds.User, userId.ToString(), ct)).ToArray();
            var groupAssignments = await assignments.ListForPrincipalsAsync(
                EntityKinds.Group, groupPrincipalIds, ct);

            var roleIds = directAssignments
                .Concat(groupAssignments)
                .Select(a => a.RoleId)
                .Distinct()
                .ToList();

            var rolesById = (await roleStore.ListByIdsAsync(roleIds, ct))
                .ToDictionary(r => r.Id);

            var roleSummaries = roleIds
                .Where(rolesById.ContainsKey)
                .Select(id => rolesById[id])
                .Select(r => (object)new { id = r.Id, name = r.Name, isSystem = r.IsSystem })
                .ToArray();

            var isSuperAdmin = roleIds.Contains(SystemRoles.SuperAdminId);

            // Coalesce per-user to a 60s sliding window. Same pattern as
            // notifications/unread-count — without it the audit firehose
            // gets one event per page navigation per user.
            if (userId != Guid.Empty
                && coalescer.ShouldPublish(userId, AuthEventTypes.MeViewed))
            {
                await auditPublisher.PublishAsync(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.MeViewed,
                    AuthEventTopic.ResourceKind,
                    resource: new { userId = userIdRaw, username = user.FindFirstValue(ClaimTypes.Name) },
                    details: new
                    {
                        roleCount = roleSummaries.Length,
                        groupCount = groups.Length,
                        isSuperAdmin,
                        coalesceWindowSeconds = 60
                    },
                    ct);
            }

            return Results.Json(new
            {
                authenticated = true,
                userId = userIdRaw,
                username = user.FindFirstValue(ClaimTypes.Name),
                firstName = user.FindFirstValue(ClaimTypes.GivenName),
                lastName = user.FindFirstValue(ClaimTypes.Surname),
                email = user.FindFirstValue(ClaimTypes.Email),
                authSource = user.FindFirstValue("auth_source"),
                idpKey = user.FindFirstValue("idp_key"),
                isSuperAdmin,
                roles = roleSummaries,
                groups
            });
        });

        group.MapPost("/logout", async (
            HttpContext context,
            IAuditEventPublisher auditPublisher) =>
        {
            // The auth cookie is SameSite=Strict, so a cross-site POST won't
            // present it and the principal here is anonymous — short-circuit so
            // we don't emit a useless audit event for what amounts to a CSRF
            // probe. Defense-in-depth alongside the cookie's own SameSite gate.
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return Results.Ok();
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = context.User.FindFirstValue(ClaimTypes.Name);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.Logout,
                AuthEventTopic.ResourceKind,
                resource: new { userId, username },
                details: null);
            return Results.Ok();
        })
        .DisableAntiforgery();

        // Batched per-instance permission lookup. The SPA uses this to gate
        // action buttons (e.g. "Complete" on a task) on a list at a time —
        // one round trip rather than one per row. Order is preserved so the
        // caller can map back to its keys.
        group.MapPost("/check", async (
            CheckPermissionsRequest request,
            HttpContext http,
            IAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { authenticated = false, results = Array.Empty<object>() });
            }

            var checks = request.Checks ?? Array.Empty<CheckPermissionItem>();
            var results = new List<object>(checks.Count);
            var allowedCount = 0;
            var deniedCount = 0;
            foreach (var c in checks)
            {
                var decision = await authorizer.AuthorizeAsync(
                    http.User, c.Action ?? string.Empty,
                    new EntityRef(c.Kind ?? string.Empty, c.Id ?? string.Empty), ct);
                if (decision.IsAllowed) allowedCount++; else deniedCount++;
                results.Add(new
                {
                    kind = c.Kind,
                    action = c.Action,
                    id = c.Id,
                    allowed = decision.IsAllowed
                });
            }

            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.PermissionChecked,
                AuthEventTopic.ResourceKind,
                resource: null,
                details: new { checkCount = checks.Count, allowedCount, deniedCount },
                ct);

            return Results.Json(new { authenticated = true, results });
        }).DisableAntiforgery();

        // Issues a fresh antiforgery token + sets the matching cookie. The
        // login form (and any other pre-auth POST that opts back into
        // antiforgery validation) calls this first, then submits the
        // returned token in the form field whose name we return.
        //
        // Anonymous-allowed because the canonical caller is the unauth login
        // page. The token is bound to the issued cookie value, so handing
        // tokens out to anonymous clients doesn't weaken anything — an
        // attacker would have to also exfiltrate the cookie from the same
        // browser, which Same-Origin and SameSite=Strict rule out.
        group.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Json(new AntiforgeryTokenResponse(
                Token: tokens.RequestToken ?? string.Empty,
                FormFieldName: tokens.FormFieldName,
                HeaderName: tokens.HeaderName ?? string.Empty));
        });

        return app;
    }

    public sealed record CheckPermissionsRequest(IReadOnlyList<CheckPermissionItem> Checks);

    public sealed record CheckPermissionItem(string? Kind, string? Action, string? Id);

    public sealed record AntiforgeryTokenResponse(
        string Token,
        string FormFieldName,
        string HeaderName);
}
