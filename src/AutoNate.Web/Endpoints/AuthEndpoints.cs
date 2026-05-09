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

        group.MapGet("/me", async (
            HttpContext context,
            IRoleStore roleStore,
            IRoleAssignmentStore assignments,
            IGroupStore groupStore,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { authenticated = false });
            }

            var userIdRaw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId = Guid.TryParse(userIdRaw, out var parsed) ? parsed : Guid.Empty;

            var groups = userId == Guid.Empty
                ? Array.Empty<object>()
                : (await groupStore.ListGroupsForUserAsync(userId, ct))
                    .Select(g => (object)new { id = g.Id, name = g.Name })
                    .ToArray();

            var groupIds = userId == Guid.Empty
                ? new List<Guid>()
                : (await groupStore.ListGroupsForUserAsync(userId, ct)).Select(g => g.Id).ToList();

            var directAssignments = userId == Guid.Empty
                ? Array.Empty<Models.Authorization.RoleAssignment>()
                : (await assignments.ListForPrincipalAsync(EntityKinds.User, userId.ToString(), ct)).ToArray();

            var groupAssignments = new List<Models.Authorization.RoleAssignment>();
            foreach (var gid in groupIds)
            {
                groupAssignments.AddRange(
                    await assignments.ListForPrincipalAsync(EntityKinds.Group, gid.ToString(), ct));
            }

            var allAssignments = directAssignments.Concat(groupAssignments).ToList();
            var roleIds = allAssignments.Select(a => a.RoleId).Distinct().ToList();

            var allRoles = await roleStore.ListAsync(ct);
            var rolesById = allRoles.ToDictionary(r => r.Id);

            var roleSummaries = roleIds
                .Where(rolesById.ContainsKey)
                .Select(id => rolesById[id])
                .Select(r => (object)new { id = r.Id, name = r.Name, isSystem = r.IsSystem })
                .ToArray();

            var isSuperAdmin = roleIds.Contains(SystemRoles.SuperAdminId);

            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.MeViewed,
                AuthEventTopic.ResourceKind,
                resource: new { userId = userIdRaw, username = user.FindFirstValue(ClaimTypes.Name) },
                details: new { roleCount = roleSummaries.Length, groupCount = groups.Length, isSuperAdmin },
                ct);

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
