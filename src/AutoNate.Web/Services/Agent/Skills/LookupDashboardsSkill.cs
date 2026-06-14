using System.Text.Json;
using AutoNate.Web.Services.Dashboards;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
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
                Description:
                    "List the widget-type catalog the SPA knows about. Returns: a universal `dataSourceShape` doc " +
                    "explaining the FOUR dataSource.type discriminator values (records | workflows | savedQuery | adHocAql) — there is NO 'dataset' value; " +
                    "a `datasetBindingPattern` doc spelling out that Datasets are queried via dataSource.type='adHocAql' with AQL like `FROM Dataset(\"name\") COLUMNS(...) GROUP(...)`; " +
                    "a `commonGotchas` doc listing per-widget binding limitations; and per-widget entries with `type`, `category`, `title`, `description`, `defaultSize`, " +
                    "`configShape` (the TRUE field names from the Zod schema), `supportedSources` (subset of records/workflows/savedQuery/adHocAql), and `notes` for gotchas.",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeListTypesAsync),

            new AgentTool(
                Name: "build_widget_config_template",
                Description:
                    "Build a complete, schema-valid widget `config` blob for a specific widget type bound to a specific data source. " +
                    "USE THIS BEFORE calling manage-dashboards.add_widget or update_widget — the widget config schemas have non-obvious field names " +
                    "(dataSource.type, savedQueryLabelColumn, recordGroupBy, etc.) that are easy to invent incorrectly. " +
                    "**For mode='adHocAql' and mode='savedQuery' the tool fully VALIDATES the AQL first**: it runs the parser + type-checker (same path as aql-assist.parse_aql), " +
                    "then verifies that `labelColumn`/`valueColumn`/`xAxisColumn`/`yAxisColumn`/`sizeColumn`/`categoryColumn`/`bucketColumn`/`compositeSeries[].valueColumn` all exist in the AQL's result schema. " +
                    "A failed validation returns a structured `error` envelope with the parser errors PLUS the actual `availableColumns` (name + dataType) so you can fix the AQL or the column name and retry — " +
                    "no half-broken widget gets persisted. On success the `validatedSchema` field of the response lists every result column so you can pick axes confidently. " +
                    "**To bind a chart to a Dataset, use mode='adHocAql' and pass the AQL query via `adHocAqlQuery` — there is NO 'dataset' mode.** " +
                    "Datasets are queried via the AQL syntax `FROM Dataset(\"name\") ORDER BY col1 COLUMNS(col1, AVG(col2)) GROUP(col1)` (NOTE the clause order: FROM → WHERE → ORDER BY → COLUMNS → GROUP → LIMIT). " +
                    "Result columns map to chart axes via `labelColumn` (becomes savedQueryLabelColumn — X axis / slice label, empty defaults to the first result column) " +
                    "and `valueColumn` (becomes savedQueryValueColumn — Y axis / slice value, empty defaults to row count). " +
                    "data-table CANNOT bind to savedQuery or adHocAql — the tool returns an error if you try.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "widgetType": { "type": "string", "description": "Registry key, e.g. 'chart-line', 'chart-bar', 'chart-composite', 'chart-quadrant', 'data-table'." },
                        "mode": { "type": "string", "enum": ["records", "workflows", "savedQuery", "adHocAql"] },
                        "adHocAqlQuery": { "type": ["string", "null"], "description": "Raw AQL text for mode='adHocAql'. Grammar: `FROM <entity>[(arg)] [WHERE ...] [ORDER BY ...] [COLUMNS(...)] [GROUP(...)] [LIMIT n]` (clauses in that exact order). For Datasets: `FROM Dataset(\"name\") COLUMNS(col1, AVG(col2) AS avg_val) GROUP(col1)`. NOT `SELECT`, NOT `GROUP BY`, NOT `TAKE` — those are SQL, not AQL." },
                        "savedQueryId": { "type": ["string", "null"], "description": "UUID for mode='savedQuery'." },
                        "recordTypeId": { "type": ["string", "null"], "description": "UUID for mode='records'; omit or empty for 'All records'." },
                        "workflowModelId": { "type": ["string", "null"], "description": "UUID for mode='workflows'; omit or empty for 'All models'." },
                        "labelColumn": { "type": ["string", "null"], "description": "Chart widgets: X-axis / label column (sets savedQueryLabelColumn). Quadrant: tooltip label." },
                        "valueColumn": { "type": ["string", "null"], "description": "Chart widgets: Y-axis / value column (sets savedQueryValueColumn)." },
                        "seriesLabel": { "type": ["string", "null"], "description": "Legend label for the series. Default 'Count' or labelColumn." },
                        "seriesColor": { "type": ["string", "null"], "description": "Mantine color token like 'teal.6', 'blue.5'. Default depends on chart type." },
                        "recordGroupBy": { "type": ["string", "null"], "description": "Chart widgets, mode=records: built-in field ('status', 'name', 'dueDate', etc.) or 'field:<key>' for custom fields." },
                        "workflowGroupBy": { "type": ["string", "null"], "enum": ["status", "model", null] },
                        "bucketColumn": { "type": ["string", "null"], "description": "chart-composite: X axis column name." },
                        "compositeSeries": {
                          "type": ["array", "null"],
                          "description": "chart-composite: 1-4 series specs.",
                          "items": {
                            "type": "object",
                            "properties": {
                              "name": { "type": "string" },
                              "type": { "type": "string", "enum": ["bar", "line", "area"] },
                              "valueColumn": { "type": "string" },
                              "aggregation": { "type": "string", "enum": ["sum", "avg", "count"] },
                              "color": { "type": "string" }
                            },
                            "required": ["name", "type", "valueColumn", "aggregation"],
                            "additionalProperties": false
                          }
                        },
                        "xAxisColumn": { "type": ["string", "null"], "description": "Quadrant family: numeric column for X." },
                        "yAxisColumn": { "type": ["string", "null"], "description": "Quadrant family: numeric column for Y." },
                        "sizeColumn": { "type": ["string", "null"], "description": "chart-bubble: numeric column for bubble size." },
                        "categoryColumn": { "type": ["string", "null"], "description": "Quadrant family: categorical column for per-point coloring." },
                        "xAxisLabel": { "type": ["string", "null"] },
                        "yAxisLabel": { "type": ["string", "null"] },
                        "pageSize": { "type": ["integer", "null"], "minimum": 5, "maximum": 200, "description": "data-table only." },
                        "includeArchived": { "type": ["boolean", "null"], "description": "data-table only." }
                      },
                      "required": ["widgetType", "mode"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeBuildWidgetTemplateAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Dashboard reads are owner-scoped in v1 — non-owners get NotFound. " +
        "WIDGET CONFIG TRUTH: every widget's `dataSource` has the SAME four-discriminator shape — `dataSource.type` is one of 'records' | 'workflows' | 'savedQuery' | 'adHocAql'. " +
        "THERE IS NO 'dataset' TYPE. To bind a widget to a Dataset you set `dataSource.type = 'adHocAql'` and put the AQL into `dataSource.adHocAqlQuery`. " +
        "AQL grammar: `FROM <entity>[(arg)] [WHERE ...] [ORDER BY ...] [COLUMNS(<items>)] [GROUP(<fields>)] [LIMIT n]` — clauses MUST appear in that order. It uses `COLUMNS(...)` not `SELECT`, `GROUP(...)` not `GROUP BY`, `LIMIT n` not `TAKE n`. " +
        "Example: `FROM Dataset(\"Weather Temperatures\") ORDER BY date COLUMNS(date, AVG(temperature_celsius) AS avg_temp) GROUP(date)`. " +
        "For chart widgets the result column → axis mapping is `savedQueryLabelColumn` (X axis / slice label) and `savedQueryValueColumn` (Y axis / value); empty `savedQueryLabelColumn` defaults to the first result column and empty `savedQueryValueColumn` defaults to row count. The naming says 'savedQuery' but they apply to BOTH `savedQuery` and `adHocAql` modes. " +
        "data-table only supports `records` and `workflows` — pick a chart widget for dataset visualizations. " +
        "WORKFLOW for creating a dataset-bound widget: (1) call list_widget_types to see available types and supportedSources, (2) call build_widget_config_template with widgetType + mode='adHocAql' + adHocAqlQuery + labelColumn/valueColumn to get a complete config blob, (3) pass that blob verbatim to manage-dashboards.add_widget. Do NOT invent config field names from scratch — the schemas have non-obvious names and the dashboard will silently fall back to 'All records' mode if the dataSource shape is wrong.";

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
    // Schemas verified directly against the Zod definitions on 2026-06-13.
    // Keep in sync when widget types are added/removed or schemas change.
    // The MantineChart variants (chart-bar, chart-line, ...) all share the
    // same mantineChartWidgetSchema — only chartType + default color differ.
    private const string MantineChartConfigShape =
        "{ chartType: 'bar' | 'line' | 'area' | 'donut' | 'pie' | 'radial-bar' | 'funnel' | 'bars-list' | 'treemap', " +
        "dataSource: <DataSource>, " +
        "recordGroupBy: string (records mode; built-in like 'status'/'name'/'dueDate' or 'field:<key>' for custom fields), " +
        "workflowGroupBy: 'status' | 'model', " +
        "recordDrillBy: string[] (default []), workflowDrillBy: ('status'|'model')[] (default []), " +
        "savedQueryLabelColumn: string (savedQuery / adHocAql: X axis / slice label; empty = first result column), " +
        "savedQueryValueColumn: string (savedQuery / adHocAql: Y axis / slice value; empty = count rows), " +
        "seriesLabel: string (legend), seriesColor: string (Mantine token like 'teal.6') }";

    private const string CompositeChartConfigShape =
        "{ dataSource: <DataSource>, " +
        "bucketColumn: string (X axis column — records: built-in or 'field:<key>'; adHocAql: result column name), " +
        "series: [{ name: string, type: 'bar' | 'line' | 'area', valueColumn: string, aggregation: 'sum' | 'avg' | 'count', color: string }] (min 1, max 4), " +
        "xAxisLabel: string, yAxisLabel: string }";

    private const string QuadrantChartConfigShape =
        "{ dataSource: <DataSource>, " +
        "xAxisColumn: string (required: numeric column or field for X), yAxisColumn: string (required: numeric for Y), " +
        "sizeColumn: string (optional: numeric for bubble size — required for chart-bubble), " +
        "labelColumn: string (per-point tooltip header), " +
        "categoryColumn: string (optional: categorical for per-point coloring), " +
        "xMidpoint: number | null (null = auto from data), yMidpoint: number | null, " +
        "quadrantLabelTopRight: string, quadrantLabelTopLeft: string, quadrantLabelBottomLeft: string, quadrantLabelBottomRight: string, " +
        "xAxisLabel: string, yAxisLabel: string, seriesColor: string, showQuadrantOverlay: boolean }";

    private const string DataTableConfigShape =
        "{ dataSource: <DataSource>, " +
        "recordColumns: ('key' | 'name' | 'status' | 'dueDate' | 'assignees' | 'updatedAtUtc')[], " +
        "workflowColumns: ('name' | 'model' | 'status' | 'currentStep' | 'startedAtUtc' | 'lastActivityAtUtc')[], " +
        "pageSize: number (5–200), includeArchived: boolean }";

    private static readonly string[] ChartSources = { "records", "savedQuery", "adHocAql" };
    private static readonly string[] DataTableSources = { "records", "workflows" };

    private static readonly WidgetCatalogEntry[] Catalog =
    {
        new("chart-bar", "Charts", "Bar chart", "Vertical bars per category. Good for counting buckets like status or owner.",
            6, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-line", "Charts", "Line chart", "Single series as a continuous line — best when categories have natural ordering (dates).",
            6, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-area", "Charts", "Area chart", "Line chart with the region under the line filled. Emphasizes magnitude.",
            6, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-donut", "Charts", "Donut chart", "Each category's share of the whole as a ring slice. Small N of buckets.",
            4, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-pie", "Charts", "Pie chart", "Solid pie slices. Donut without the hole.",
            4, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-radial-bar", "Charts", "Radial bar chart", "Concentric arcs, one per bucket. Eye-catching for small N.",
            4, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-funnel", "Charts", "Funnel chart", "Ordered stages narrowing top-to-bottom.",
            4, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-bars-list", "Charts", "Horizontal bars list", "Horizontal bars sized relative to the biggest entry — reads like a top-N leaderboard.",
            4, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-treemap", "Charts", "Treemap", "Nested rectangles sized by value. Many categories at once.",
            4, 4, MantineChartConfigShape, ChartSources, null),
        new("chart-composite", "Charts", "Composite chart",
            "Mixed bar + line + area series on the same axes (e.g. count of records as bars + average score as a line).",
            6, 5, CompositeChartConfigShape, ChartSources, null),
        new("chart-quadrant", "Charts", "Quadrant chart",
            "Scatter with quadrant overlay + midpoint reference lines + corner labels.",
            6, 5, QuadrantChartConfigShape, ChartSources, null),
        new("chart-bubble", "Charts", "Bubble chart",
            "Quadrant chart with overlay off and sizeColumn driving bubble size.",
            6, 5, QuadrantChartConfigShape, ChartSources,
            "Set showQuadrantOverlay=false and provide sizeColumn."),
        new("chart-scatter", "Charts", "Scatter chart",
            "Quadrant chart with overlay off (no corner labels, no midpoint lines).",
            6, 5, QuadrantChartConfigShape, ChartSources,
            "Set showQuadrantOverlay=false."),
        new("data-table", "Tables", "Data table",
            "Tabular view of records or workflow executions with sortable, filterable columns.",
            8, 4, DataTableConfigShape, DataTableSources,
            "DOES NOT support savedQuery or adHocAql. For dataset visualizations use a chart widget instead."),
        new("mantine-chart", "Charts", "Mantine chart (legacy combined)",
            "Legacy combined chart entry with chartType-in-form. Use the per-type chart-* entries for new widgets.",
            6, 4, MantineChartConfigShape, ChartSources, null)
    };

    private const string DataSourceShapeDoc =
        "Every widget's `dataSource` is the same four-discriminator shape: " +
        "{ type: 'records' | 'workflows' | 'savedQuery' | 'adHocAql', " +
        "recordTypeId: string (empty='All records' — used only when type='records'), " +
        "workflowModelId: string (empty='All models' — used only when type='workflows'), " +
        "savedQueryId: string (UUID — used only when type='savedQuery'), " +
        "adHocAqlQuery: string (raw AQL text — used only when type='adHocAql') }. " +
        "Every field must be present on the persisted blob — for unused modes pass empty strings (e.g. mode='adHocAql' still includes recordTypeId='', workflowModelId='', savedQueryId='').";

    private const string DatasetBindingPatternDoc =
        "There is NO 'dataset' value for dataSource.type. To bind a widget to a Dataset, set dataSource.type='adHocAql' and write the AQL. " +
        "AQL is NOT SQL: clauses are `FROM <entity>[(arg)] [WHERE ...] [ORDER BY ...] [COLUMNS(<items>)] [GROUP(<fields>)] [LIMIT n]` in that EXACT order. " +
        "Use `COLUMNS(...)` NOT `SELECT`, `GROUP(...)` NOT `GROUP BY`, `LIMIT n` NOT `TAKE n`. Aliases use `AS` (e.g. `COUNT() AS Total`). " +
        "Working example: `FROM Dataset(\"Your Dataset Name\") ORDER BY col1 COLUMNS(col1, AVG(col2) AS avg_col2) GROUP(col1)`. " +
        "For chart widgets, also set savedQueryLabelColumn (X axis / label) and savedQueryValueColumn (Y axis / value) to the result column names you want plotted — empty savedQueryLabelColumn defaults to the first result column, empty savedQueryValueColumn defaults to row count. The 'savedQuery' prefix is historical; the fields apply to BOTH savedQuery and adHocAql binding.";

    private const string CommonGotchasDoc =
        "* data-table ONLY supports dataSource.type 'records' or 'workflows'. To visualize a Dataset, use a chart widget. " +
        "* If the dashboard shows 'All records isn't supported yet — pick a record type in widget settings', the widget's dataSource shape is wrong (likely missing or invalid type discriminator); the SPA fell back to records mode. " +
        "* `savedQueryLabelColumn` and `savedQueryValueColumn` apply to both savedQuery AND adHocAql modes — the legacy naming hides that. " +
        "* `recordGroupBy` accepts built-in record fields ('status', 'name', 'dueDate', 'key', 'assigneeCount') OR custom fields with `field:<fieldKey>` prefix. " +
        "* `seriesColor` is a Mantine color token like 'teal.6', 'blue.5' — NOT a hex code.";

    private static Task<JsonElement> InvokeListTypesAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var widgets = Catalog.Select(e => new
        {
            type = e.Type,
            category = e.Category,
            title = e.Title,
            description = e.Description,
            defaultSize = new { w = e.DefaultW, h = e.DefaultH },
            configShape = e.ConfigShape,
            supportedSources = e.SupportedSources,
            notes = e.Notes
        }).ToList();

        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            kind = "widget_catalog",
            source = "hand-maintained mirror of src/widgets/*/...config.ts (verified 2026-06-13)",
            data = new
            {
                dataSourceShape = DataSourceShapeDoc,
                datasetBindingPattern = DatasetBindingPatternDoc,
                commonGotchas = CommonGotchasDoc,
                widgets
            }
        }));
    }

    private sealed record WidgetCatalogEntry(
        string Type,
        string Category,
        string Title,
        string Description,
        int DefaultW,
        int DefaultH,
        string ConfigShape,
        IReadOnlyList<string> SupportedSources,
        string? Notes);

    // chartType discriminator per registered chart-* entry. The Zod schema
    // accepts a single chartType field; each registry entry pre-bakes the
    // chartType so the picker doesn't need a redundant dropdown.
    private static readonly Dictionary<string, string> ChartTypePerWidget = new(StringComparer.Ordinal)
    {
        { "chart-bar", "bar" },
        { "chart-line", "line" },
        { "chart-area", "area" },
        { "chart-donut", "donut" },
        { "chart-pie", "pie" },
        { "chart-radial-bar", "radial-bar" },
        { "chart-funnel", "funnel" },
        { "chart-bars-list", "bars-list" },
        { "chart-treemap", "treemap" },
        { "mantine-chart", "bar" }
    };

    // Default seriesColor per chart entry (matches the picker thumbnails).
    private static readonly Dictionary<string, string> DefaultColorPerWidget = new(StringComparer.Ordinal)
    {
        { "chart-bar", "teal.6" },
        { "chart-line", "blue.5" },
        { "chart-area", "orange.5" },
        { "chart-donut", "teal.6" },
        { "chart-pie", "teal.6" },
        { "chart-radial-bar", "teal.6" },
        { "chart-funnel", "teal.6" },
        { "chart-bars-list", "teal.6" },
        { "chart-treemap", "teal.6" },
        { "mantine-chart", "teal.6" }
    };

    private static async Task<JsonElement> InvokeBuildWidgetTemplateAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "build_widget_config_template";
        var widgetType = ReadString(args, "widgetType");
        if (string.IsNullOrWhiteSpace(widgetType))
            return Error(action, "widgetType is required.");
        var mode = ReadString(args, "mode");
        if (mode is not ("records" or "workflows" or "savedQuery" or "adHocAql"))
            return Error(action, "mode must be 'records' | 'workflows' | 'savedQuery' | 'adHocAql'.");

        var catalogEntry = Catalog.FirstOrDefault(e => string.Equals(e.Type, widgetType, StringComparison.Ordinal));
        if (catalogEntry is null)
            return Error(action,
                $"Unknown widgetType '{widgetType}'. Call list_widget_types to see the catalog.");
        if (!catalogEntry.SupportedSources.Contains(mode))
            return Error(action,
                $"Widget '{widgetType}' does not support dataSource.type='{mode}'. Supported: [{string.Join(", ", catalogEntry.SupportedSources)}]. " +
                (widgetType == "data-table"
                    ? "For dataset visualizations use a chart widget (chart-line, chart-bar, chart-composite, etc.)."
                    : string.Empty));

        // Read mode-specific args + defaults.
        var adHocAqlQuery = ReadString(args, "adHocAqlQuery") ?? string.Empty;
        var savedQueryId = ReadString(args, "savedQueryId") ?? string.Empty;
        var recordTypeId = ReadString(args, "recordTypeId") ?? string.Empty;
        var workflowModelId = ReadString(args, "workflowModelId") ?? string.Empty;
        if (mode == "adHocAql" && string.IsNullOrWhiteSpace(adHocAqlQuery))
            return Error(action,
                "mode='adHocAql' requires `adHocAqlQuery`. AQL grammar: `FROM <entity>[(arg)] [WHERE ...] [ORDER BY ...] [COLUMNS(...)] [GROUP(...)] [LIMIT n]` — NOT SQL. To bind to a Dataset: `FROM Dataset(\"name\") ORDER BY col1 COLUMNS(col1, AVG(col2)) GROUP(col1)`.");
        if (mode == "savedQuery" && string.IsNullOrWhiteSpace(savedQueryId))
            return Error(action,
                "mode='savedQuery' requires `savedQueryId` (UUID). Use lookup-aql.list_saved_queries to find one.");

        var dataSource = new
        {
            type = mode,
            recordTypeId,
            workflowModelId,
            savedQueryId,
            adHocAqlQuery
        };

        // Build the config blob per widget family, then hand it to the
        // shared WidgetConfigValidator. This way the build-template path
        // and the manage-dashboards add/update paths run the SAME parse +
        // column-existence checks no matter how the config was composed.
        object? config = null;
        string? schemaName = null;

        if (ChartTypePerWidget.TryGetValue(widgetType, out var chartType))
        {
            var labelColumn = ReadString(args, "labelColumn") ?? string.Empty;
            var valueColumn = ReadString(args, "valueColumn") ?? string.Empty;
            var seriesLabel = ReadString(args, "seriesLabel");
            if (string.IsNullOrEmpty(seriesLabel))
                seriesLabel = string.IsNullOrEmpty(labelColumn) ? "Count" : labelColumn;
            var seriesColor = ReadString(args, "seriesColor")
                ?? DefaultColorPerWidget.GetValueOrDefault(widgetType, "teal.6");
            var recordGroupBy = ReadString(args, "recordGroupBy") ?? "status";
            var workflowGroupBy = ReadString(args, "workflowGroupBy") ?? "status";
            if (workflowGroupBy is not ("status" or "model"))
                return Error(action, "workflowGroupBy must be 'status' or 'model'.");
            config = new
            {
                chartType,
                dataSource,
                recordGroupBy,
                workflowGroupBy,
                recordDrillBy = Array.Empty<string>(),
                workflowDrillBy = Array.Empty<string>(),
                savedQueryLabelColumn = labelColumn,
                savedQueryValueColumn = valueColumn,
                seriesLabel,
                seriesColor
            };
            schemaName = "mantineChartWidgetSchema";
        }
        else if (widgetType == "chart-composite")
        {
            var bucketColumn = ReadString(args, "bucketColumn") ?? ReadString(args, "labelColumn") ?? string.Empty;
            var xAxisLabel = ReadString(args, "xAxisLabel") ?? string.Empty;
            var yAxisLabel = ReadString(args, "yAxisLabel") ?? string.Empty;
            var series = ReadCompositeSeries(args)
                ?? new[] { new { name = "Count", type = "bar", valueColumn = "", aggregation = "count", color = "teal.6" } }
                    .Cast<object>().ToList();
            if (series.Count is 0 or > 4)
                return Error(action, "compositeSeries must contain 1–4 entries.");
            config = new
            {
                dataSource,
                bucketColumn,
                series,
                xAxisLabel,
                yAxisLabel
            };
            schemaName = "compositeChartWidgetSchema";
        }
        else if (widgetType is "chart-quadrant" or "chart-bubble" or "chart-scatter")
        {
            var xAxisColumn = ReadString(args, "xAxisColumn") ?? string.Empty;
            var yAxisColumn = ReadString(args, "yAxisColumn") ?? string.Empty;
            if (string.IsNullOrEmpty(xAxisColumn) || string.IsNullOrEmpty(yAxisColumn))
                return Error(action, $"{widgetType} requires both xAxisColumn and yAxisColumn.");
            var sizeColumn = ReadString(args, "sizeColumn") ?? string.Empty;
            if (widgetType == "chart-bubble" && string.IsNullOrEmpty(sizeColumn))
                return Error(action, "chart-bubble requires sizeColumn (drives bubble size).");
            var labelColumn = ReadString(args, "labelColumn") ?? string.Empty;
            var categoryColumn = ReadString(args, "categoryColumn") ?? string.Empty;
            var xAxisLabel = ReadString(args, "xAxisLabel") ?? string.Empty;
            var yAxisLabel = ReadString(args, "yAxisLabel") ?? string.Empty;
            var seriesColor = ReadString(args, "seriesColor") ?? "teal.6";
            config = new
            {
                dataSource,
                xAxisColumn,
                yAxisColumn,
                sizeColumn,
                labelColumn,
                categoryColumn,
                xMidpoint = (double?)null,
                yMidpoint = (double?)null,
                quadrantLabelTopRight = "High X / High Y",
                quadrantLabelTopLeft = "Low X / High Y",
                quadrantLabelBottomLeft = "Low X / Low Y",
                quadrantLabelBottomRight = "High X / Low Y",
                xAxisLabel,
                yAxisLabel,
                seriesColor,
                showQuadrantOverlay = widgetType == "chart-quadrant"
            };
            schemaName = "quadrantChartWidgetSchema";
        }
        else if (widgetType == "data-table")
        {
            var pageSize = ReadInt(args, "pageSize") ?? 25;
            if (pageSize < 5) pageSize = 5;
            if (pageSize > 200) pageSize = 200;
            var includeArchived = args.TryGetProperty("includeArchived", out var ia) && ia.ValueKind == JsonValueKind.True;
            config = new
            {
                dataSource,
                recordColumns = new[] { "key", "name", "status", "updatedAtUtc" },
                workflowColumns = new[] { "name", "model", "status", "lastActivityAtUtc" },
                pageSize,
                includeArchived
            };
            schemaName = "dataTableWidgetSchema";
        }
        else
        {
            return Error(action,
                $"Template builder doesn't have a handler for widgetType '{widgetType}' yet. The catalog entry is correct but the config is not auto-buildable — read its `configShape` from list_widget_types and compose the blob manually.");
        }

        var configElement = JsonSerializer.SerializeToElement(config);
        var result = await WidgetConfigValidator.ValidateAsync(widgetType, configElement, ctx, ct);
        if (!result.Ok) return FormatValidationFailure(action, result);
        return SuccessEnvelope(widgetType, mode, config!, schemaName!, catalogEntry, result.ValidatedSchema);
    }

    // Format a validator outcome as an error envelope appropriate for this
    // skill (Error / { kind:"error", source, data:{...} }). The Manage tools
    // format the same result as a ConfirmGate.Failed envelope; see
    // ManageDashboardsSkill.BuildValidationFailedEnvelope.
    internal static JsonElement FormatValidationFailure(string action, WidgetConfigValidator.Result r)
    {
        if (r.ParserErrors is not null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "error",
                source = action,
                data = new
                {
                    message = r.Message,
                    sourceField = r.SourceField,
                    queryText = r.QueryText,
                    parserErrors = r.ParserErrors,
                    grammarHint = WidgetConfigValidator.GrammarHint
                }
            });
        }
        if (r.AvailableColumns is not null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "error",
                source = action,
                data = new
                {
                    message = r.Message,
                    fieldName = r.SourceField,
                    badValue = r.BadValue,
                    schemaSource = r.SchemaSource,
                    availableColumns = r.AvailableColumns.Select(c => new
                    {
                        name = c.Name,
                        dataType = c.DataType.ToString().ToLowerInvariant(),
                        isAggregable = c.IsAggregable
                    }).ToList()
                }
            });
        }
        return Error(action, r.Message ?? "Validation failed.");
    }

    private static JsonElement SuccessEnvelope(
        string widgetType, string mode, object config, string schemaName,
        WidgetCatalogEntry entry, IReadOnlyList<QueryColumn>? validatedSchema)
    {
        var schemaOut = validatedSchema?.Select(c => new
        {
            name = c.Name,
            dataType = c.DataType.ToString().ToLowerInvariant(),
            isAggregable = c.IsAggregable
        }).ToList();
        return JsonSerializer.SerializeToElement(new
        {
            kind = "widget_config_template",
            source = schemaName,
            data = new
            {
                widgetType,
                mode,
                config,
                defaultSize = new { w = entry.DefaultW, h = entry.DefaultH },
                validatedSchema = schemaOut
            }
        });
    }

    private static int? ReadInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static List<object>? ReadCompositeSeries(JsonElement args)
    {
        if (!args.TryGetProperty("compositeSeries", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return null;
        var list = new List<object>(arr.GetArrayLength());
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            list.Add(new
            {
                name = entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "",
                type = entry.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "bar",
                valueColumn = entry.TryGetProperty("valueColumn", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "",
                aggregation = entry.TryGetProperty("aggregation", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : "sum",
                color = entry.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "teal.6"
            });
        }
        return list;
    }

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
