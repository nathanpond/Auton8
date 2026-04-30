using System.Security.Claims;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Services.Audit;

// Request-scoped facade over HttpContext that hands out the values every
// audit-grade event needs. Concrete impl reads through IHttpContextAccessor
// lazily so it works whether a request is in flight or not — background
// services that resolve this from a scoped service provider get sensible
// "system" defaults (empty strings, null actor) instead of throwing.
public interface IRequestContext
{
    Guid? ActorId { get; }
    string? ActorUserName { get; }
    string RequestId { get; }
    string? CorrelationId { get; }
    string IpAddress { get; }
    string UserAgent { get; }
    string HttpMethod { get; }
    string RoutePath { get; }

    // Builds the AuditContext that goes into every event envelope. Callers
    // that already have an actorId in hand (typical for store-layer mutations
    // that capture it from claims at the endpoint boundary) pass it in;
    // otherwise pass null and the actor is read from the request claims.
    AuditContext BuildAuditContext(
        Guid? actorIdOverride = null,
        DateTimeOffset? occurredAtUtc = null,
        AuthOutcome outcome = AuthOutcome.Allowed,
        string? denyReason = null,
        string? sourceAppId = null);
}

public sealed class RequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    private const int MaxUserAgentLength = 512;
    private const string DefaultSourceAppId = "autonate.web";

    public Guid? ActorId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public string? ActorUserName =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public string RequestId => httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty;

    public string? CorrelationId
    {
        get
        {
            var headers = httpContextAccessor.HttpContext?.Request.Headers;
            if (headers is null) return null;
            // Standard names that operators use; first match wins.
            if (headers.TryGetValue("X-Correlation-Id", out var x) && x.Count > 0) return x[0];
            if (headers.TryGetValue("X-Request-Id", out var r) && r.Count > 0) return r[0];
            return null;
        }
    }

    public string IpAddress
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx is null) return string.Empty;
            // Honor X-Forwarded-For only when present; the leftmost value is
            // the originating client per the spec. No allow-list is enforced
            // here — that's a deployment concern handled by the proxy.
            if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd)
                && fwd.Count > 0
                && !string.IsNullOrWhiteSpace(fwd[0]))
            {
                var first = fwd[0]!.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(first)) return first;
            }
            return ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }
    }

    public string UserAgent
    {
        get
        {
            var headers = httpContextAccessor.HttpContext?.Request.Headers;
            if (headers is null) return string.Empty;
            if (!headers.TryGetValue("User-Agent", out var ua) || ua.Count == 0) return string.Empty;
            var raw = ua[0] ?? string.Empty;
            return raw.Length <= MaxUserAgentLength ? raw : raw.Substring(0, MaxUserAgentLength);
        }
    }

    public string HttpMethod => httpContextAccessor.HttpContext?.Request.Method ?? string.Empty;

    public string RoutePath
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx is null) return string.Empty;
            // Route template if available (PII-safe — no path values); otherwise
            // fall back to the literal path.
            var endpoint = ctx.GetEndpoint();
            if (endpoint is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint)
            {
                var template = routeEndpoint.RoutePattern.RawText;
                if (!string.IsNullOrEmpty(template)) return template!;
            }
            return ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : string.Empty;
        }
    }

    public AuditContext BuildAuditContext(
        Guid? actorIdOverride = null,
        DateTimeOffset? occurredAtUtc = null,
        AuthOutcome outcome = AuthOutcome.Allowed,
        string? denyReason = null,
        string? sourceAppId = null) => new(
        ActorId: actorIdOverride ?? ActorId,
        ActorUserName: ActorUserName,
        OccurredAtUtc: occurredAtUtc ?? DateTimeOffset.UtcNow,
        RequestId: RequestId,
        CorrelationId: CorrelationId,
        IpAddress: IpAddress,
        UserAgent: UserAgent,
        SourceAppId: sourceAppId ?? DefaultSourceAppId,
        HttpMethod: HttpMethod,
        RoutePath: RoutePath,
        AuthOutcome: outcome,
        AuthDecisionReason: denyReason);
}

// Out-of-request fallback. Used by background services that resolve
// IRequestContext from a scope they created themselves (no HttpContext).
// Values are empty strings / null actor so AuditContext.BuildAuditContext
// still returns a well-formed record; callers can override actorIdOverride
// when they have a system-assigned actor (e.g. workflow recorder).
public sealed class SystemRequestContext : IRequestContext
{
    public Guid? ActorId => null;
    public string? ActorUserName => null;
    public string RequestId => string.Empty;
    public string? CorrelationId => null;
    public string IpAddress => string.Empty;
    public string UserAgent => string.Empty;
    public string HttpMethod => string.Empty;
    public string RoutePath => string.Empty;

    public AuditContext BuildAuditContext(
        Guid? actorIdOverride = null,
        DateTimeOffset? occurredAtUtc = null,
        AuthOutcome outcome = AuthOutcome.Allowed,
        string? denyReason = null,
        string? sourceAppId = null) => new(
        ActorId: actorIdOverride,
        ActorUserName: null,
        OccurredAtUtc: occurredAtUtc ?? DateTimeOffset.UtcNow,
        RequestId: string.Empty,
        CorrelationId: null,
        IpAddress: string.Empty,
        UserAgent: string.Empty,
        SourceAppId: sourceAppId ?? "autonate.web",
        HttpMethod: string.Empty,
        RoutePath: string.Empty,
        AuthOutcome: outcome,
        AuthDecisionReason: denyReason);
}
