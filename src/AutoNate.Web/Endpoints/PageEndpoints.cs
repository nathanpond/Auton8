using AutoNate.Web.Services.Menus;

namespace AutoNate.Web.Endpoints;

public static class PageEndpoints
{
    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pages").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IMenuStore store, CancellationToken ct) =>
            Results.Ok(await store.ListPagesAsync(http.User, ct)));

        group.MapGet("/lookup", async (
            string path,
            HttpContext http,
            IMenuStore store,
            CancellationToken ct) =>
        {
            var page = await store.GetPageByPathAsync(path, http.User, ct);
            return page is null ? Results.NotFound() : Results.Ok(page);
        });

        return app;
    }
}
