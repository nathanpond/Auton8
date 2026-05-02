using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Menus;
using AutoNate.Web.Services.SiteSettings;

namespace AutoNate.Web.Endpoints;

public static class PageEndpoints
{
    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pages").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http,
            IMenuStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var pages = await store.ListPagesAsync(http.User, ct);
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.PageListViewed,
                SiteResourceKinds.Page,
                resource: null,
                details: new { resultCount = pages.Count },
                ct);
            return Results.Ok(pages);
        });

        group.MapGet("/lookup", async (
            string path,
            HttpContext http,
            IMenuStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var page = await store.GetPageByPathAsync(path, http.User, ct);
            if (page is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.PageLookupViewed,
                SiteResourceKinds.Page,
                resource: new { id = page.Id, path = page.Path, contentType = page.ContentType },
                details: null,
                ct);
            return Results.Ok(page);
        });

        return app;
    }
}
