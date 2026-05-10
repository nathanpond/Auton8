using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Dapr;
using AutoNate.Web.Services.SystemHealth;

namespace AutoNate.Web.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
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
