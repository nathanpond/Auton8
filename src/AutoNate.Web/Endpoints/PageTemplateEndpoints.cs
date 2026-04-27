using AutoNate.Web.Models.Menus;
using AutoNate.Web.Services.Menus;

namespace AutoNate.Web.Endpoints;

public static class PageTemplateEndpoints
{
    public static IEndpointRouteBuilder MapPageTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/page-templates").RequireAuthorization();

        group.MapGet("/", async (IPageTemplateStore store, CancellationToken ct) =>
        {
            var templates = await store.ListEnabledAsync(ct);
            var dto = templates
                .Select(t => new PageTemplateDto(t.Key, t.Name, t.Description, t.DefaultPath))
                .ToList();
            return Results.Ok(dto);
        });

        return app;
    }

    public sealed record PageTemplateDto(string Key, string Name, string? Description, string DefaultPath);
}
