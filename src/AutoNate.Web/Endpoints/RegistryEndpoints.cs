using AutoNate.Web.Authorization;

namespace AutoNate.Web.Endpoints;

// Exposes the IEntityRegistry as JSON so the SPA's selector builder can
// populate kind, action, and tag dropdowns dynamically. Keep this read-only
// and inexpensive — it's queried on page load.
public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/registry").RequireAuthorization();

        group.MapGet("/", (IEntityRegistry registry) =>
        {
            var kinds = registry.All
                .OrderBy(t => t.Kind, StringComparer.Ordinal)
                .Select(t => new
                {
                    kind = t.Kind,
                    actions = t.Actions.OrderBy(a => a, StringComparer.Ordinal).ToArray(),
                    tags = t.Tags.OrderBy(g => g, StringComparer.Ordinal).ToArray()
                })
                .ToArray();
            return Results.Ok(new { kinds });
        });

        return app;
    }
}
