using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

public static class PermissionGrantEndpoints
{
    public static IEndpointRouteBuilder MapPermissionGrantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/grants").RequireAuthorization();

        group.MapGet("/", async (
            string? principalKind,
            string? principalId,
            IPermissionGrantStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(principalKind) && !string.IsNullOrEmpty(principalId))
            {
                var scoped = await store.ListForPrincipalAsync(principalKind, principalId, ct);
                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.PermissionGrantListViewed,
                    IamResourceKinds.PermissionGrant,
                    resource: null,
                    details: new { resultCount = scoped.Count, scope = "by-principal", principalKind, principalId },
                    ct);
                return Results.Ok(scoped);
            }

            var all = await store.ListAsync(ct);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.PermissionGrantListViewed,
                IamResourceKinds.PermissionGrant,
                resource: null,
                details: new { resultCount = all.Count, scope = "all" },
                ct);
            return Results.Ok(all);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        // Paged variant — same shape as /api/users/page. pageSize=0 is the
        // count probe used by the SPA's auto-mode DataTable.
        group.MapGet("/page", async (
            int? page,
            int? pageSize,
            string? q,
            string? sort,
            string? sortDir,
            string? principalKind,
            string? effect,
            IPermissionGrantStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var request = new ListPermissionGrantsRequest(
                Page: page ?? 0,
                PageSize: pageSize ?? 25,
                Search: q,
                SortBy: sort,
                SortDir: sortDir,
                PrincipalKind: principalKind,
                Effect: effect);
            var result = await store.ListPagedAsync(request, ct);
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.PermissionGrantListViewed,
                IamResourceKinds.PermissionGrant,
                resource: null,
                details: new
                {
                    resultCount = result.Items.Count,
                    totalCount = result.TotalCount,
                    page = request.Page,
                    pageSize = request.PageSize,
                    search = request.Search,
                    principalKind = request.PrincipalKind,
                    effect = request.Effect
                },
                ct);
            return Results.Ok(result);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        group.MapPost("/", async (
            CreateGrantRequest request,
            HttpContext http,
            IPermissionGrantStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var grant = await store.CreateAsync(
                    new CreatePermissionGrantInput(
                        request.PrincipalKind,
                        request.PrincipalId,
                        request.Action,
                        request.SelectorString,
                        request.Effect,
                        request.Priority),
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
                        effect = request.Effect
                    },
                    details: new { selectorString = request.SelectorString, priority = request.Priority },
                    ct);
                return Results.Created($"/api/admin/grants/{grant.Id}", grant);
            }
            catch (PermissionGrantValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, IPermissionGrantStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            if (!deleted) return Results.NotFound();
            await auditPublisher.PublishAsync(
                IamEventTopic.TopicName,
                IamEventTypes.PermissionGrantDeleted,
                IamResourceKinds.PermissionGrant,
                resource: new { id },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.SiteConfig, Actions.Edit);

        return app;
    }
    public sealed record CreateGrantRequest(
        string PrincipalKind,
        string PrincipalId,
        string Action,
        string SelectorString,
        string Effect,
        int Priority);
}
