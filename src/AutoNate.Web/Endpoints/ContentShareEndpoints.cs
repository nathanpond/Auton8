using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Share-a-page endpoints — paired with the Share button in the page header.
// Preview: given a list of candidate userIds, report whether each can view the
// target page today plus whether the calling user owns the project. Commit:
// for users who already have view access, send an in-app notification; for
// users who don't, optionally create a per-page allow grant first (owner-only)
// then notify. Non-owners can still call commit but users without access are
// returned in `skippedUserIds` instead of being notified, so the SPA can warn
// the sharer.
//
// Per-page grants are written to permission_grants with a `/page/{id}`
// selector. The existing ContentAuthorizer ancestor-closest-override engine
// already resolves these at depth 0, so a viewer with no project membership
// can still pull /api/content/pages/{id} and the embedded notes.
//
// Notes are intentionally not shared directly: they live inside a page, so
// sharing the parent page is the unit. The UI exposes Share on both the page
// tab and any note tab, both targeting the page.
public static class ContentShareEndpoints
{
    public static IEndpointRouteBuilder MapContentShareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/pages/{pageId:guid}/share")
            .RequireAuthorization();

        group.MapPost("/preview", async (
            Guid pageId,
            SharePreviewRequest request,
            HttpContext http,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var projectId = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == ContentKinds.Page
                             && ca.DescendantId == pageId
                             && ca.AncestorKind == ContentKinds.Project)
                .Select(ca => (Guid?)ca.AncestorId)
                .FirstOrDefaultAsync(ct);
            if (projectId is null)
            {
                return Results.NotFound(new { error = "Page not found." });
            }

            var isOwner = await authorizer.IsProjectOwnerAsync(http.User, projectId.Value, ct);

            var users = new List<UserAccess>();
            foreach (var userId in (request.UserIds ?? new List<Guid>()).Distinct())
            {
                var decision = await authorizer.AuthorizeAsync(
                    SyntheticPrincipal(userId),
                    ContentKinds.Page, pageId, Actions.View, ct);
                users.Add(new UserAccess(userId, decision.IsAllowed));
            }

            return Results.Ok(new SharePreviewResponse(isOwner, users));
        }).RequirePermission(EntityKinds.Page, Actions.View, "pageId")
          .DisableAntiforgery();

        group.MapPost("/", async (
            Guid pageId,
            ShareRequest request,
            HttpContext http,
            IContentAuthorizer authorizer,
            IPermissionGrantStore grantStore,
            INotificationStore notifications,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var page = await db.Pages.AsNoTracking()
                .Where(p => p.Id == pageId)
                .Select(p => new { p.Id, p.Locator, p.Title })
                .FirstOrDefaultAsync(ct);
            if (page is null) return Results.NotFound(new { error = "Page not found." });

            var projectId = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == ContentKinds.Page
                             && ca.DescendantId == pageId
                             && ca.AncestorKind == ContentKinds.Project)
                .Select(ca => (Guid?)ca.AncestorId)
                .FirstOrDefaultAsync(ct);
            if (projectId is null)
            {
                return Results.BadRequest(new { error = "Page has no project ancestor." });
            }

            var isOwner = await authorizer.IsProjectOwnerAsync(http.User, projectId.Value, ct);
            if (request.GrantAccess && !isOwner)
            {
                return Results.Forbid();
            }

            // Resolve a friendly "shared by" name for the notification body.
            var actor = await db.LocalUsers.AsNoTracking()
                .Where(u => u.UserId == actorId)
                .Select(u => new { u.FirstName, u.LastName, u.Username })
                .FirstOrDefaultAsync(ct);
            var actorName = ResolveDisplayName(actor?.FirstName, actor?.LastName, actor?.Username);

            var userIds = (request.UserIds ?? new List<Guid>())
                .Distinct()
                .Where(u => u != actorId) // never notify the sharer themselves
                .ToList();
            var notified = new List<Guid>();
            var skipped = new List<Guid>();
            var granted = new List<Guid>();

            foreach (var userId in userIds)
            {
                var canView = (await authorizer.AuthorizeAsync(
                    SyntheticPrincipal(userId),
                    ContentKinds.Page, pageId, Actions.View, ct)).IsAllowed;

                if (!canView && request.GrantAccess)
                {
                    // Per-page allow grant. Selector is the canonical
                    // `/page/{guid}` path — ContentAuthorizer resolves it at
                    // depth 0 of the ancestor chain.
                    var selector = $"/page/{pageId}";
                    try
                    {
                        await grantStore.CreateAsync(
                            new CreatePermissionGrantInput(
                                PrincipalKind: EntityKinds.User,
                                PrincipalId: userId.ToString(),
                                Action: Actions.View,
                                SelectorString: selector,
                                Effect: "allow",
                                Priority: 100),
                            actorId, ct);
                        granted.Add(userId);
                        canView = true;
                    }
                    catch (PermissionGrantValidationException)
                    {
                        // Bad selector / principal — count as skipped, don't
                        // block the rest of the batch.
                    }
                }

                if (canView)
                {
                    await notifications.CreateAsync(new CreateNotificationInput(
                        UserId: userId,
                        Kind: NotificationKinds.PageShared,
                        Title: $"{actorName} shared a page with you",
                        Body: $"{actorName} shared \"{page.Title}\".",
                        RelatedEntityKind: "page",
                        RelatedEntityId: pageId.ToString(),
                        LinkPath: $"/notes/{page.Locator}"
                    ), ct);
                    notified.Add(userId);
                }
                else
                {
                    skipped.Add(userId);
                }
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                "content.page.shared",
                ContentResourceKinds.Page,
                resource: new { id = pageId, title = page.Title },
                details: new
                {
                    notified = notified.Count,
                    skipped = skipped.Count,
                    granted = granted.Count,
                    grantAccess = request.GrantAccess
                },
                ct);

            return Results.Ok(new ShareResponse(notified, skipped, granted));
        }).DisableAntiforgery();

        return app;
    }

    // Builds a ClaimsPrincipal carrying just the user's NameIdentifier claim
    // so we can reuse IContentAuthorizer.AuthorizeAsync to evaluate access
    // on behalf of arbitrary users (preview is a check, not a real call from
    // them — the actor is the sharer).
    private static ClaimsPrincipal SyntheticPrincipal(Guid userId)
    {
        var identity = new ClaimsIdentity("synthetic");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        return new ClaimsPrincipal(identity);
    }

    private static string ResolveDisplayName(string? first, string? last, string? username)
    {
        var name = $"{first} {last}".Trim();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (!string.IsNullOrWhiteSpace(username)) return username!;
        return "Someone";
    }

    public sealed record SharePreviewRequest(List<Guid> UserIds);
    public sealed record UserAccess(Guid UserId, bool CanView);
    public sealed record SharePreviewResponse(bool IsOwner, List<UserAccess> Users);
    public sealed record ShareRequest(List<Guid> UserIds, bool GrantAccess);
    public sealed record ShareResponse(
        List<Guid> NotifiedUserIds,
        List<Guid> SkippedUserIds,
        List<Guid> GrantedUserIds);
}
