using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

// Phase 9 polish — resource-scoped permission overrides for folders +
// documents (self-service, not site-admin). These let someone who can edit
// a folder/document share it (or widen access) by creating a permission
// grant targeting just that resource, without needing the site-admin Grants
// page (`/api/admin/grants`, gated by SiteConfig.Edit).
//
// The underlying store is the same `IPermissionGrantStore` the admin page
// uses, but these endpoints clamp it hard so a resource editor can't escalate:
//   • selector is FORCED to `/{kind}/{id}` of the resource in the route —
//     the caller never supplies it, so they can't target other resources;
//   • effect is FORCED to "allow" — denies (which can lock out owners) stay
//     admin-only;
//   • action must be in a per-kind allowlist AND the caller must already
//     hold that action on the resource (you can't grant what you don't have);
//   • list/delete only see/touch grants whose selector matches THIS resource.
// Folder grants still cascade to descendants via the content_ancestors
// closure — that's the whole point of sharing a folder.
public static class ContentPermissionOverrideEndpoints
{
    // Actions a resource editor may hand out via self-service. Deliberately
    // excludes delete/archive (destructive) — those stay admin-only.
    private static readonly string[] DocumentGrantableActions =
        { Actions.View, Actions.Comment, Actions.Edit };
    private static readonly string[] FolderGrantableActions =
        { Actions.View, Actions.Edit, Actions.Create };

    public static IEndpointRouteBuilder MapContentPermissionOverrideEndpoints(
        this IEndpointRouteBuilder app)
    {
        MapForKind(app, "documents", ContentKinds.Document,
            EntityKinds.Document, DocumentGrantableActions);
        MapForKind(app, "folders", ContentKinds.Folder,
            EntityKinds.Folder, FolderGrantableActions);
        return app;
    }

    private static void MapForKind(
        IEndpointRouteBuilder app,
        string segment,
        string kind,
        string entityKind,
        string[] grantableActions)
    {
        // Fixed route-param name `id` so it binds to the shared handlers'
        // `Guid id` parameter (minimal APIs bind route params by name). The
        // `segment` (documents/folders) is what differentiates the routes.
        var group = app.MapGroup($"/api/content/{segment}/{{id:guid}}/permissions");

        string Selector(Guid id) => $"/{kind}/{id}";

        // List the overrides targeting exactly this resource. Managing access
        // is an edit-level concern, so the whole group requires Edit.
        group.MapGet("/", async (
            Guid id,
            IPermissionGrantStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var selector = Selector(id);
            var all = await store.ListAsync(ct);
            var items = all
                .Where(g => string.Equals(g.SelectorString, selector, StringComparison.OrdinalIgnoreCase))
                .ToList();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.PermissionGrantListViewed,
                IamResourceKinds.PermissionGrant,
                resource: new { kind, resourceId = id },
                details: new { resultCount = items.Count, scope = "by-resource" },
                ct);
            return Results.Ok(new PermissionOverrideListResponse(items, grantableActions));
        }).RequirePermission(entityKind, Actions.Edit, "id");

        // Create an allow-override on this resource for a principal + action.
        group.MapPost("/", async (
            Guid id,
            CreateOverrideRequest request,
            HttpContext http,
            IPermissionGrantStore store,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!grantableActions.Contains(request.Action))
            {
                return Results.BadRequest(new
                {
                    error = $"'{request.Action}' can't be granted here. Allowed: {string.Join(", ", grantableActions)}."
                });
            }

            // Escalation guard: you can only grant an action you yourself are
            // allowed to perform on this resource. The Edit gate on the route
            // proves edit; this re-checks the SPECIFIC action being handed out.
            var decision = await authorizer.AuthorizeAsync(http.User, kind, id, request.Action, ct);
            if (!decision.IsAllowed)
            {
                return Results.Json(
                    new { error = $"You don't have '{request.Action}' on this item, so you can't grant it." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Self-service sharing is for people and groups of people.
            //
            // The store's allowlist also contains `role`, because an admin
            // granting through /api/admin/grants legitimately targets roles —
            // but nothing narrowed it here, so a folder or document editor
            // could attach a resource grant to a role, SuperAdmin included,
            // through an endpoint whose own header comment describes user/group
            // sharing (archived-186). The route is reachable by anyone with Edit on the
            // item, which is a much lower bar than administering roles.
            if (!string.Equals(request.PrincipalKind, EntityKinds.User, StringComparison.Ordinal)
                && !string.Equals(request.PrincipalKind, EntityKinds.Group, StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    error = $"principalKind must be '{EntityKinds.User}' or '{EntityKinds.Group}'. "
                            + "Granting to a role is an administrative action — use /api/admin/grants."
                });
            }

            try
            {
                var grant = await store.CreateAsync(
                    new CreatePermissionGrantInput(
                        request.PrincipalKind,
                        request.PrincipalId,
                        request.Action,
                        Selector(id),   // forced — caller never picks the selector
                        "allow",        // forced — denies are admin-only
                        Priority: 0),
                    http.GetActorId(), ct);

                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.PermissionGrantCreated,
                    IamResourceKinds.PermissionGrant,
                    resource: new
                    {
                        id = grant.Id,
                        principalKind = request.PrincipalKind,
                        principalId = request.PrincipalId,
                        action = request.Action,
                        effect = "allow"
                    },
                    details: new { selectorString = Selector(id), scope = "resource-override", kind, resourceId = id },
                    ct);
                return Results.Created($"/api/content/{segment}/{id}/permissions/{grant.Id}", grant);
            }
            catch (PermissionGrantValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequirePermission(entityKind, Actions.Edit, "id");

        // Remove an override. We verify the grant targets THIS resource before
        // deleting, so an editor can't delete arbitrary grants (e.g. admin
        // grants on other resources) by guessing ids.
        group.MapDelete("/{grantId:guid}", async (
            Guid id,
            Guid grantId,
            IPermissionGrantStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var selector = Selector(id);
            var all = await store.ListAsync(ct);
            var match = all.FirstOrDefault(g =>
                g.Id == grantId &&
                string.Equals(g.SelectorString, selector, StringComparison.OrdinalIgnoreCase));
            if (match is null) return Results.NotFound();

            var deleted = await store.DeleteAsync(grantId, ct);
            if (!deleted) return Results.NotFound();

            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.PermissionGrantDeleted,
                IamResourceKinds.PermissionGrant,
                resource: new { id = grantId },
                details: new { scope = "resource-override", kind, resourceId = id },
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(entityKind, Actions.Edit, "id");
    }

    public sealed record CreateOverrideRequest(
        string PrincipalKind,
        string PrincipalId,
        string Action);

    public sealed record PermissionOverrideListResponse(
        IReadOnlyList<PermissionGrant> Items,
        IReadOnlyList<string> GrantableActions);
}
