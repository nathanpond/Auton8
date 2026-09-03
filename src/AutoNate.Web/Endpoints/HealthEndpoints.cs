using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Dapr;
using AutoNate.Web.Services.SystemHealth;

namespace AutoNate.Web.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness, and the one deliberately unauthenticated endpoint in this
        // group.
        //
        // A container healthcheck carries no credentials, so it cannot use the
        // two endpoints below — and they are gated correctly: they expose
        // component status, internal topology and exception messages. The
        // answer is a probe that reveals nothing rather than loosening those.
        //
        // It returns a bare 200 on purpose. No version, no component status, no
        // configuration: anything added here is readable by anyone who can
        // reach the port, which in a container deployment is anything on the
        // same network. `.AllowAnonymous()` is explicit so
        // AuthorizationGatePresenceTests sees a deliberate decision rather than
        // a route that slipped through without one (project invariant 3).
        app.MapGet("/api/health/live", () => Results.Ok())
            .AllowAnonymous()
            .WithName("HealthLive");

        var group = app.MapGroup("/api/health")
            .RequireAuthorization();

        // Both endpoints expose infrastructure detail (component status,
        // exception messages, internal topology) consumed only by admin
        // screens. Gate on SiteConfig:View to match /api/admin/grants and
        // /api/admin/explain — the platform's other debug surfaces.
        group.MapGet("/dapr", async (DaprSidecarProbe probe, CancellationToken cancellationToken) =>
        {
            var available = await probe.IsAvailableAsync(cancellationToken);
            return Results.Ok(new { available });
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        group.MapGet("/system", async (SystemHealthService systemHealth, CancellationToken cancellationToken) =>
        {
            var report = await systemHealth.CheckAsync(cancellationToken);
            return Results.Ok(report);
        }).RequireKindPermission(EntityKinds.SiteConfig, Actions.View);

        return app;
    }
}
