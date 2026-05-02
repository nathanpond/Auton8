using AutoNate.Web.Models.Menus;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Menus;
using AutoNate.Web.Services.SiteSettings;

namespace AutoNate.Web.Endpoints;

public static class PageTemplateEndpoints
{
    public static IEndpointRouteBuilder MapPageTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/page-templates").RequireAuthorization();

        group.MapGet("/", async (
            IPageTemplateStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var templates = await store.ListEnabledAsync(ct);
            var dto = templates
                .Select(t => new PageTemplateDto(t.Key, t.Name, t.Description, t.DefaultPath))
                .ToList();
            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.PageTemplateListViewed,
                SiteResourceKinds.PageTemplate,
                resource: null,
                details: new { resultCount = dto.Count },
                ct);
            return Results.Ok(dto);
        });

        return app;
    }

    public sealed record PageTemplateDto(string Key, string Name, string? Description, string DefaultPath);
}
