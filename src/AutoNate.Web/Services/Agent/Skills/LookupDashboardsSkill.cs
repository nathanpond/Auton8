using System.Text.Json;
using AutoNate.Web.Services.Dashboards;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only dashboard inspection. Visibility is enforced by IDashboardStore's
// per-actor methods (v1 = owned-only; share rows + visibility tiers are
// future-proofing). No flat Dataset:View / Dashboard:View gate exists for
// these surfaces; the store filters silently.
public sealed class LookupDashboardsSkill : IAgentSkill
{
    public string Name => "lookup-dashboards";

    public string Description =>
        "List the actor's dashboards, fetch one with its widgets, and enumerate the widget-type catalog.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupDashboardsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_dashboards",
                Description: "List dashboards visible to the current user. Optional `search` is a case-insensitive name substring; `includeArchived` defaults to false.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "search": { "type": ["string", "null"] },
                        "includeArchived": { "type": ["boolean", "null"] }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_dashboard",
                Description: "Fetch one dashboard by id, including its widgets (positions, titles, and full config blobs).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetAsync),

            new AgentTool(
                Name: "get_widget",
                Description: "Fetch a single widget's full record (type, title, config, grid position).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dashboardId": { "type": "string" },
                        "widgetId": { "type": "string" }
                      },
                      "required": ["dashboardId", "widgetId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetWidgetAsync),

            new AgentTool(
                Name: "list_widget_types",
                Description: "List the widget-type catalog the SPA knows about. Returns `type`, `category`, `title`, `description`, `defaultSize`, and a short summary of the config schema for each.",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeListTypesAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Dashboard reads are owner-scoped in v1 — non-owners get NotFound. Widget config shapes are owned by the SPA's Zod schemas; this skill exposes a catalog summary via list_widget_types but defers detailed config validation to manage-dashboards.add_widget / update_widget which round-trip through the endpoint.";

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var search = ReadString(args, "search");
        var includeArchived = args.TryGetProperty("includeArchived", out var ia)
            && ia.ValueKind == JsonValueKind.True;

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var rows = await store.ListForActorAsync(ctx.Session.UserId, ct);
        IEnumerable<Persistence.Scaffolded.Dashboard> filtered = rows;
        if (!includeArchived) filtered = filtered.Where(d => !d.IsArchived);
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(d => d.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var data = filtered.Select(d => new
        {
            id = d.Id,
            ownerUserId = d.OwnerUserId,
            name = d.Name,
            description = d.Description,
            visibility = d.Visibility,
            scope = d.Scope,
            source = d.Source,
            templateKey = d.TemplateKey,
            isArchived = d.IsArchived,
            updatedAtUtc = d.UpdatedAtUtc
        }).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "dashboards",
            source = "IDashboardStore",
            data
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("get_dashboard", "id is required.");
        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var found = await store.GetForActorAsync(id, ctx.Session.UserId, ct);
        if (found is null) return Error("get_dashboard", $"Dashboard {id} not found or not accessible.");

        var d = found.Dashboard;
        var widgets = found.Widgets.Select(w => new
        {
            id = w.Id,
            widgetType = w.WidgetType,
            title = w.Title,
            config = ParseJson(w.ConfigJsonb),
            gridX = w.GridX,
            gridY = w.GridY,
            gridW = w.GridW,
            gridH = w.GridH,
            sortOrder = w.SortOrder,
            createdAtUtc = w.CreatedAtUtc,
            updatedAtUtc = w.UpdatedAtUtc
        }).ToList();

        return JsonSerializer.SerializeToElement(new
        {
            kind = "dashboard",
            source = "IDashboardStore",
            data = new
            {
                id = d.Id,
                ownerUserId = d.OwnerUserId,
                name = d.Name,
                description = d.Description,
                visibility = d.Visibility,
                scope = d.Scope,
                source = d.Source,
                templateKey = d.TemplateKey,
                settings = ParseJson(d.SettingsJsonb),
                isArchived = d.IsArchived,
                createdAtUtc = d.CreatedAtUtc,
                updatedAtUtc = d.UpdatedAtUtc,
                widgets
            }
        });
    }

    private static async Task<JsonElement> InvokeGetWidgetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "dashboardId", out var dashboardId))
            return Error("get_widget", "dashboardId is required.");
        if (!TryReadGuid(args, "widgetId", out var widgetId))
            return Error("get_widget", "widgetId is required.");

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var found = await store.GetForActorAsync(dashboardId, ctx.Session.UserId, ct);
        if (found is null) return Error("get_widget", $"Dashboard {dashboardId} not found or not accessible.");
        var w = found.Widgets.FirstOrDefault(x => x.Id == widgetId);
        if (w is null) return Error("get_widget", $"Widget {widgetId} not found on dashboard {dashboardId}.");

        return JsonSerializer.SerializeToElement(new
        {
            kind = "dashboard_widget",
            source = "IDashboardStore",
            data = new
            {
                id = w.Id,
                dashboardId = w.DashboardId,
                widgetType = w.WidgetType,
                title = w.Title,
                config = ParseJson(w.ConfigJsonb),
                gridX = w.GridX,
                gridY = w.GridY,
                gridW = w.GridW,
                gridH = w.GridH,
                sortOrder = w.SortOrder,
                createdAtUtc = w.CreatedAtUtc,
                updatedAtUtc = w.UpdatedAtUtc
            }
        });
    }

    // Hand-maintained mirror of src/AutoNate.Spa/src/widgets/*/...config.ts.
    // Keep in sync when widget types are added/removed. We don't need the
    // full Zod schemas server-side — just enough so the bot can pick a type
    // and form a plausible config that the SPA endpoint will validate.
    private static readonly WidgetCatalogEntry[] Catalog =
    {
        new("data-table", "Data", "Data table", "Tabular view bound to a saved query or dataset.",
            8, 4,
            "{ dataSource: { kind: 'saved-query' | 'dataset' | 'record-type', id?: string, savedQueryName?: string, datasetName?: string, recordTypeKey?: string }, columns?: string[], pageSize?: number }"),
        new("mantine-chart", "Charts", "Mantine chart (bar/line/area/donut/pie/etc.)",
            "One widget that supports bar, line, area, donut, pie, radial-bar, funnel, bars-list, and treemap variants via the `variant` field.",
            6, 4,
            "{ dataSource: ..., variant: 'bar' | 'line' | 'area' | 'donut' | 'pie' | 'radial-bar' | 'funnel' | 'bars-list' | 'treemap', categoryColumn: string, valueColumns: string[], colorTokens?: string[], stacked?: boolean }"),
        new("chart-bar", "Charts", "Bar chart", "Pre-set variant of mantine-chart with variant='bar'.",
            6, 4, "Same shape as mantine-chart with variant fixed to 'bar'."),
        new("chart-line", "Charts", "Line chart", "Pre-set variant of mantine-chart with variant='line'.",
            6, 4, "Same shape as mantine-chart with variant fixed to 'line'."),
        new("chart-area", "Charts", "Area chart", "Pre-set variant of mantine-chart with variant='area'.",
            6, 4, "Same shape as mantine-chart with variant fixed to 'area'."),
        new("chart-donut", "Charts", "Donut chart", "Pre-set variant of mantine-chart with variant='donut'.",
            4, 4, "Same shape as mantine-chart with variant fixed to 'donut'."),
        new("chart-pie", "Charts", "Pie chart", "Pre-set variant of mantine-chart with variant='pie'.",
            4, 4, "Same shape as mantine-chart with variant fixed to 'pie'."),
        new("chart-radial-bar", "Charts", "Radial bar chart", "Pre-set variant of mantine-chart.",
            4, 4, "Same shape as mantine-chart with variant fixed to 'radial-bar'."),
        new("chart-funnel", "Charts", "Funnel chart", "Pre-set variant of mantine-chart.",
            4, 4, "Same shape as mantine-chart with variant fixed to 'funnel'."),
        new("chart-bars-list", "Charts", "Horizontal bars list", "Pre-set variant of mantine-chart.",
            4, 4, "Same shape as mantine-chart with variant fixed to 'bars-list'."),
        new("chart-treemap", "Charts", "Treemap", "Pre-set variant of mantine-chart.",
            4, 4, "Same shape as mantine-chart with variant fixed to 'treemap'."),
        new("chart-quadrant", "Charts", "Quadrant chart",
            "Scatter with quadrant overlay + midpoint reference lines + corner labels.",
            6, 5,
            "{ dataSource: ..., xAxisColumn: string, yAxisColumn: string, sizeColumn?: string, labelColumn?: string, categoryColumn?: string, xMidpoint?: number|null, yMidpoint?: number|null, quadrantLabelTopRight?: string, quadrantLabelTopLeft?: string, quadrantLabelBottomLeft?: string, quadrantLabelBottomRight?: string, xAxisLabel?: string, yAxisLabel?: string, seriesColor?: string, showQuadrantOverlay?: boolean }"),
        new("chart-bubble", "Charts", "Bubble chart",
            "Quadrant-chart variant with showQuadrantOverlay=false and sizeColumn required.",
            6, 5,
            "Same shape as chart-quadrant; sizeColumn drives bubble size, showQuadrantOverlay defaults to false."),
        new("chart-scatter", "Charts", "Scatter chart",
            "Quadrant-chart variant without the overlay (no corner labels, no midpoint lines).",
            6, 5,
            "Same shape as chart-quadrant with showQuadrantOverlay=false."),
        new("chart-composite", "Charts", "Composite chart",
            "Mixed bar + line series on the same axes (e.g. revenue bars + 7-day trend line).",
            6, 4,
            "{ dataSource: ..., categoryColumn: string, series: [{ name: string, type: 'bar' | 'line', valueColumn: string, aggregation: 'count' | 'sum' | 'avg' | 'min' | 'max', color?: string }] }")
    };

    private static Task<JsonElement> InvokeListTypesAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var data = Catalog.Select(e => new
        {
            type = e.Type,
            category = e.Category,
            title = e.Title,
            description = e.Description,
            defaultSize = new { w = e.DefaultW, h = e.DefaultH },
            configShape = e.ConfigShape
        }).ToList();

        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            kind = "widget_catalog",
            source = "hand-maintained mirror of src/widgets/registry.ts",
            data
        }));
    }

    private sealed record WidgetCatalogEntry(
        string Type,
        string Category,
        string Title,
        string Description,
        int DefaultW,
        int DefaultH,
        string ConfigShape);

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        return args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            && Guid.TryParse(v.GetString(), out id);
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement ParseJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
