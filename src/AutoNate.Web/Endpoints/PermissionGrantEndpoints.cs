using System.Security.Claims;
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
        });

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
                    ActorId(http), ct);
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
        }).DisableAntiforgery();

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
        }).DisableAntiforgery();

        return app;
    }

    private static Guid ActorId(HttpContext http)
    {
        var raw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public sealed record CreateGrantRequest(
        string PrincipalKind,
        string PrincipalId,
        string Action,
        string SelectorString,
        string Effect,
        int Priority);
}
