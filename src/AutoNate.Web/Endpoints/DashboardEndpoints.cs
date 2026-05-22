using System.Text.Json;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Dashboards;
using AutoNate.Web.Services.Events;

namespace AutoNate.Web.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var rows = await store.ListForActorAsync(actorId, ct);
            var dto = rows.Select(MapDashboard).ToList();

            await auditPublisher.PublishAsync(
                DashboardEventTopic.TopicName,
                DashboardEventTypes.DashboardListViewed,
                DashboardResourceKinds.Dashboard,
                resource: null,
                details: new { resultCount = dto.Count },
                ct);

            return Results.Ok(dto);
        }).AuthorizedInHandler(
            "Result set filtered to dashboards the actor owns OR is shared on (v1: owned-only since dashboard_shares is empty).");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var found = await store.GetForActorAsync(id, actorId, ct);
            if (found is null) return Results.NotFound();

            await auditPublisher.PublishAsync(
                DashboardEventTopic.TopicName,
                DashboardEventTypes.DashboardViewed,
                DashboardResourceKinds.Dashboard,
                resource: new { id = found.Dashboard.Id, name = found.Dashboard.Name },
                details: new { widgetCount = found.Widgets.Count },
                ct);

            return Results.Ok(new DashboardWithWidgetsDto(
                MapDashboard(found.Dashboard),
                found.Widgets.Select(MapWidget).ToList()));
        }).AuthorizedInHandler(
            "Store returns null when actor isn't the owner and has no share row; maps to 404 so the row stays invisible.");

        group.MapPost("/", async (
            CreateDashboardRequest request,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Dashboard name is required." });

            Dashboard dashboard;
            try
            {
                dashboard = await store.CreateAsync(
                    new CreateDashboardInput(request.Name, request.Description, request.FromMountPath),
                    actorId, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                DashboardEventTopic.TopicName,
                DashboardEventTypes.DashboardCreated,
                DashboardResourceKinds.Dashboard,
                resource: new { id = dashboard.Id, name = dashboard.Name },
                details: new { fromMountPath = request.FromMountPath, source = dashboard.Source },
                ct);

            return Results.Created($"/api/dashboards/{dashboard.Id}", MapDashboard(dashboard));
        }).DisableAntiforgery()
          .OpenToAuthenticated(
              "Any signed-in user can create their own dashboard; ownership is " +
              "captured in dashboards.owner_user_id at insert time.");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateDashboardRequest request,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var dashboard = await store.UpdateAsync(
                    id,
                    new UpdateDashboardInput(request.Name, request.Description, request.Settings),
                    actorId, ct);

                await auditPublisher.PublishAsync(
                    DashboardEventTopic.TopicName,
                    DashboardEventTypes.DashboardUpdated,
                    DashboardResourceKinds.Dashboard,
                    resource: new { id = dashboard.Id, name = dashboard.Name },
                    details: null,
                    ct);

                return Results.Ok(MapDashboard(dashboard));
            }
            catch (DashboardNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Store enforces owner-only edit; returns NotFound for both " +
              "missing rows and non-owners.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetForActorAsync(id, actorId, ct);
            if (existing is null) return Results.NotFound();
            var ok = await store.DeleteAsync(id, actorId, ct);
            if (!ok) return Results.NotFound();

            await auditPublisher.PublishAsync(
                DashboardEventTopic.TopicName,
                DashboardEventTypes.DashboardDeleted,
                DashboardResourceKinds.Dashboard,
                resource: new { id, name = existing.Dashboard.Name },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Store enforces owner-only delete; cascades clear widgets + " +
              "shares automatically via FK.");

        group.MapPost("/{id:guid}/widgets", async (
            Guid id,
            CreateWidgetRequest request,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.WidgetType))
                return Results.BadRequest(new { error = "Widget type is required." });

            try
            {
                var widget = await store.AddWidgetAsync(id,
                    new CreateWidgetInput(
                        request.WidgetType,
                        request.Title,
                        request.Config,
                        request.GridX,
                        request.GridY,
                        request.GridW <= 0 ? 4 : request.GridW,
                        request.GridH <= 0 ? 3 : request.GridH),
                    actorId, ct);

                await auditPublisher.PublishAsync(
                    DashboardEventTopic.TopicName,
                    DashboardEventTypes.WidgetAdded,
                    DashboardResourceKinds.DashboardWidget,
                    resource: new { id = widget.Id, dashboardId = id, widgetType = widget.WidgetType },
                    details: null,
                    ct);

                return Results.Created(
                    $"/api/dashboards/{id}/widgets/{widget.Id}",
                    MapWidget(widget));
            }
            catch (DashboardNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).DisableAntiforgery()
          .AuthorizedInHandler("Owner-only — store throws DashboardNotFoundException for non-owners.");

        group.MapPatch("/{id:guid}/widgets/{widgetId:guid}", async (
            Guid id,
            Guid widgetId,
            UpdateWidgetRequest request,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var widget = await store.UpdateWidgetAsync(id, widgetId,
                    new UpdateWidgetInput(
                        request.Title,
                        request.Config,
                        request.GridX,
                        request.GridY,
                        request.GridW,
                        request.GridH),
                    actorId, ct);

                await auditPublisher.PublishAsync(
                    DashboardEventTopic.TopicName,
                    DashboardEventTypes.WidgetUpdated,
                    DashboardResourceKinds.DashboardWidget,
                    resource: new { id = widget.Id, dashboardId = id, widgetType = widget.WidgetType },
                    details: null,
                    ct);

                return Results.Ok(MapWidget(widget));
            }
            catch (DashboardNotFoundException) { return Results.NotFound(); }
            catch (DashboardWidgetNotFoundException) { return Results.NotFound(); }
        }).DisableAntiforgery()
          .AuthorizedInHandler("Owner-only — store throws DashboardNotFoundException for non-owners.");

        group.MapDelete("/{id:guid}/widgets/{widgetId:guid}", async (
            Guid id,
            Guid widgetId,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var ok = await store.RemoveWidgetAsync(id, widgetId, actorId, ct);
            if (!ok) return Results.NotFound();

            await auditPublisher.PublishAsync(
                DashboardEventTopic.TopicName,
                DashboardEventTypes.WidgetRemoved,
                DashboardResourceKinds.DashboardWidget,
                resource: new { id = widgetId, dashboardId = id },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler("Owner-only — store returns false for non-owners which maps to 404.");

        group.MapPost("/{id:guid}/layout", async (
            Guid id,
            ReplaceLayoutRequest request,
            HttpContext http,
            IDashboardStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var positions = request.Positions?
                .Select(p => new LayoutPosition(p.WidgetId, p.GridX, p.GridY, p.GridW, p.GridH))
                .ToList() ?? [];
            try
            {
                var updated = await store.ReplaceLayoutAsync(id, positions, actorId, ct);

                await auditPublisher.PublishAsync(
                    DashboardEventTopic.TopicName,
                    DashboardEventTypes.LayoutUpdated,
                    DashboardResourceKinds.Dashboard,
                    resource: new { id },
                    details: new { updatedCount = updated, requestedCount = positions.Count },
                    ct);

                return Results.Ok(new { updated });
            }
            catch (DashboardNotFoundException) { return Results.NotFound(); }
        }).DisableAntiforgery()
          .AuthorizedInHandler("Owner-only — store throws DashboardNotFoundException for non-owners.");

        return app;
    }

    private static DashboardDto MapDashboard(Dashboard d) => new(
        d.Id, d.OwnerUserId, d.Name, d.Description,
        d.Visibility, d.Scope, d.Source, d.TemplateKey,
        ParseSettings(d.SettingsJsonb),
        d.IsArchived, d.CreatedAtUtc, d.UpdatedAtUtc, d.CreatedBy, d.UpdatedBy);

    private static DashboardWidgetDto MapWidget(DashboardWidget w) => new(
        w.Id, w.DashboardId, w.WidgetType, w.Title,
        ParseConfig(w.ConfigJsonb),
        w.GridX, w.GridY, w.GridW, w.GridH, w.SortOrder,
        w.CreatedAtUtc, w.UpdatedAtUtc);

    private static JsonElement ParseSettings(string raw)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    private static JsonElement ParseConfig(string raw) => ParseSettings(raw);

    public sealed record CreateDashboardRequest(string Name, string? Description, string? FromMountPath);

    public sealed record UpdateDashboardRequest(string? Name, string? Description, JsonElement? Settings);

    public sealed record CreateWidgetRequest(
        string WidgetType,
        string? Title,
        JsonElement Config,
        int GridX,
        int GridY,
        int GridW,
        int GridH);

    public sealed record UpdateWidgetRequest(
        string? Title,
        JsonElement? Config,
        int? GridX,
        int? GridY,
        int? GridW,
        int? GridH);

    public sealed record LayoutPositionDto(Guid WidgetId, int GridX, int GridY, int GridW, int GridH);

    public sealed record ReplaceLayoutRequest(List<LayoutPositionDto>? Positions);

    public sealed record DashboardDto(
        Guid Id, Guid OwnerUserId, string Name, string? Description,
        string Visibility, string Scope, string Source, string? TemplateKey,
        JsonElement Settings,
        bool IsArchived, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record DashboardWidgetDto(
        Guid Id, Guid DashboardId, string WidgetType, string? Title,
        JsonElement Config,
        int GridX, int GridY, int GridW, int GridH, int SortOrder,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    public sealed record DashboardWithWidgetsDto(
        DashboardDto Dashboard,
        List<DashboardWidgetDto> Widgets);
}
