using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Http;

namespace AutoNate.Web.Authorization.EndpointFilters;

// Kind-level filter for actions like "create" that don't have a target id yet.
// Approves the request if at least one allow grant matches kind+action with no
// matching deny on the wildcard id "*". Built on top of FilterQueryAsync by
// asking it to filter a constructed one-row queryable for kind=*; if anything
// passes, the kind+action grants exist.
public sealed class RequireKindPermissionFilter : IEndpointFilter
{
    private readonly string _kind;
    private readonly string _action;

    public RequireKindPermissionFilter(string kind, string action)
    {
        _kind = kind;
        _action = action;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        var authorizer = http.RequestServices.GetRequiredService<IAuthorizer>();

        // Treat kind-level checks as "any matching allow grant present?". We
        // route through AuthorizeAsync with a wildcard id so the same dry-run
        // logging applies. A wildcard id never finds a real entity so when
        // enforcement is off it allows; when on it returns the dry-run path
        // unless an explicit kind-level allow exists.
        var decision = await authorizer.AuthorizeAsync(
            http.User,
            _action,
            new EntityRef(_kind, "*"),
            http.RequestAborted);

        if (!decision.IsAllowed)
        {
            var auditPublisher = http.RequestServices.GetRequiredService<IAuditEventPublisher>();
            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.AccessDenied,
                AuthEventTopic.ResourceKind,
                resource: new { kind = _kind, id = "*", action = _action },
                details: new { reason = decision.Reason, scope = "kind" },
                http.RequestAborted);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(invocation);
    }
}
