using AutoNate.Web.Services.Dapr;
using AutoNate.Web.Services.SystemHealth;

namespace AutoNate.Web.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/health")
            .RequireAuthorization();

        group.MapGet("/dapr", async (DaprSidecarProbe probe, CancellationToken cancellationToken) =>
        {
            var available = await probe.IsAvailableAsync(cancellationToken);
            return Results.Ok(new { available });
        });

        group.MapGet("/system", async (SystemHealthService systemHealth, CancellationToken cancellationToken) =>
        {
            var report = await systemHealth.CheckAsync(cancellationToken);
            return Results.Ok(report);
        });

        return app;
    }
}
