using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoNate.Web.Authorization.EndpointFilters;

public static class RequirePermissionExtensions
{
    // Gates an endpoint on instance-level authorization. By default the
    // target id is read from the route value `id`; pass `idFrom` to extract
    // it from elsewhere (e.g., a different route slot or a request body).
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        string kind,
        string action,
        string routeName = "id")
    {
        return builder.AddEndpointFilter(new RequirePermissionFilter(
            kind,
            action,
            ctx => ctx.HttpContext.GetRouteValue(routeName)?.ToString()));
    }

    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        string kind,
        string action,
        Func<EndpointFilterInvocationContext, string?> idFrom)
    {
        return builder.AddEndpointFilter(new RequirePermissionFilter(kind, action, idFrom));
    }

    public static RouteHandlerBuilder RequireKindPermission(
        this RouteHandlerBuilder builder,
        string kind,
        string action)
    {
        return builder.AddEndpointFilter(new RequireKindPermissionFilter(kind, action));
    }
}
