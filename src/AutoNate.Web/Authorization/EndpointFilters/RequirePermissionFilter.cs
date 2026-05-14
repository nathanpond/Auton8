using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Http;

namespace AutoNate.Web.Authorization.EndpointFilters;

// IEndpointFilter that calls IAuthorizer.AuthorizeAsync before an endpoint
// runs. When the authorizer denies, the request short-circuits with 403.
// Resolves the target id from the route by default; pass an explicit `idFrom`
// for endpoints whose target id is in the body or a sub-route.
public sealed class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _kind;
    private readonly string _action;
    private readonly Func<EndpointFilterInvocationContext, string?> _idFrom;

    public RequirePermissionFilter(
        string kind,
        string action,
        Func<EndpointFilterInvocationContext, string?> idFrom)
    {
        _kind = kind;
        _action = action;
        _idFrom = idFrom;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var auditPublisher = http.RequestServices.GetRequiredService<IAuditEventPublisher>();

        var id = _idFrom(invocation);
        if (string.IsNullOrEmpty(id))
        {
            // Without a target id we can only safely refuse to evaluate.
            // Endpoints that don't carry an id should use a kind-level filter
            // that doesn't reach AuthorizeAsync at all.
            await PublishDenied(auditPublisher, "*", "missing_target_id", http.RequestAborted);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        AuthDecision decision;
        if (ContentKinds.IsContentKind(_kind))
        {
            // Content kinds (project/cabinet/notebook/page) route through
            // IContentAuthorizer for closest-ancestor inheritance + role
            // baseline + deletions_locked enforcement.
            var contentAuthorizer = http.RequestServices.GetRequiredService<IContentAuthorizer>();
            if (!Guid.TryParse(id, out var resourceId))
            {
                await PublishDenied(auditPublisher, id, "invalid_resource_id", http.RequestAborted);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            decision = await contentAuthorizer.AuthorizeAsync(
                http.User, _kind, resourceId, _action, http.RequestAborted);
        }
        else
        {
            var authorizer = http.RequestServices.GetRequiredService<IAuthorizer>();
            decision = await authorizer.AuthorizeAsync(
                http.User,
                _action,
                new EntityRef(_kind, id),
                http.RequestAborted);
        }

        if (!decision.IsAllowed)
        {
            await PublishDenied(auditPublisher, id, decision.Reason, http.RequestAborted);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(invocation);
    }

    private Task PublishDenied(
        IAuditEventPublisher auditPublisher,
        string id,
        string reason,
        CancellationToken cancellationToken) =>
        auditPublisher.PublishAsync(
            AuthEventTopic.TopicName,
            AuthEventTypes.AccessDenied,
            AuthEventTopic.ResourceKind,
            resource: new { kind = _kind, id, action = _action },
            details: new { reason },
            cancellationToken);
}
