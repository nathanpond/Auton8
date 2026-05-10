using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace AutoNate.Web.Authorization.EndpointFilters;

// Three explicit ways an endpoint can satisfy the Layer-1 gate-presence
// check (AuthorizationGatePresenceTests):
//   1. RequirePermission / RequireKindPermission — an endpoint filter.
//   2. AuthorizedInHandler("...") — handler authorizes inline (e.g.
//      FilterQueryAsync, actor-scoped query, in-handler AuthorizeAsync).
//   3. OpenToAuthenticated("...") — any signed-in user; the rationale must
//      stand on its own without leaking cross-tenant info.
// AllowAnonymous() also satisfies the check, but covers a separate case
// (no auth at all, e.g. /login).
//
// The reason string travels with the endpoint metadata so the audit-
// authorization skill can produce a punch list keyed on rationale.

public enum AuthorizationDecisionKind
{
    InlineInHandler,
    OpenToAuthenticated
}

public sealed class AuthorizationDecisionMetadata
{
    public AuthorizationDecisionMetadata(AuthorizationDecisionKind kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Kind = kind;
        Reason = reason;
    }

    public AuthorizationDecisionKind Kind { get; }

    public string Reason { get; }
}

public static class AuthorizationDecisionExtensions
{
    // Mark an endpoint whose handler does the authorization itself. The
    // reason string should describe how — `FilterQueryAsync(...)`,
    // `actor-scoped query against ...`, `in-handler AuthorizeAsync(...)`,
    // etc. — so a future reviewer can verify the inline check still holds.
    public static RouteHandlerBuilder AuthorizedInHandler(
        this RouteHandlerBuilder builder, string reason)
    {
        return builder.WithMetadata(new AuthorizationDecisionMetadata(
            AuthorizationDecisionKind.InlineInHandler, reason));
    }

    public static RouteGroupBuilder AuthorizedInHandler(
        this RouteGroupBuilder builder, string reason)
    {
        return builder.WithMetadata(new AuthorizationDecisionMetadata(
            AuthorizationDecisionKind.InlineInHandler, reason));
    }

    // Mark an endpoint that intentionally requires only sign-in. Use this
    // sparingly: the rationale must justify why no per-actor or per-resource
    // gate is needed (typically: pure system catalog, SPA shell metadata,
    // or render path that intentionally serves all signed-in users).
    public static RouteHandlerBuilder OpenToAuthenticated(
        this RouteHandlerBuilder builder, string reason)
    {
        return builder.WithMetadata(new AuthorizationDecisionMetadata(
            AuthorizationDecisionKind.OpenToAuthenticated, reason));
    }

    public static RouteGroupBuilder OpenToAuthenticated(
        this RouteGroupBuilder builder, string reason)
    {
        return builder.WithMetadata(new AuthorizationDecisionMetadata(
            AuthorizationDecisionKind.OpenToAuthenticated, reason));
    }
}
