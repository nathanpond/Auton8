using System.Text.Json;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Dashboards;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Confirm-gated dashboard + widget CRUD. Authorization is store-side
// (owner-only in v1) — non-owner mutations surface as DashboardNotFoundException
// and we relay that as ConfirmGate.Failed. Widget config validation is
// pass-through: we hand the JSON to the store as a JsonElement and let the
// SPA's Zod schemas validate on render. The system prompt fragment + the
// list_widget_types tool in lookup-dashboards give the bot enough catalog
// info to compose a plausible config.
public sealed class ManageDashboardsSkill : IAgentSkill
{
    public string Name => "manage-dashboards";

    public string Description =>
        "Create/update/delete dashboards, add/update/remove widgets, and reposition layout.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageDashboardsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "create_dashboard",
                Description: "Create a new dashboard owned by the current user. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "description": { "type": ["string", "null"] },
                        "fromMountPath": { "type": ["string", "null"], "description": "If set, scaffold from a registered page-template mount path." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateDashboardAsync),

            new AgentTool(
                Name: "update_dashboard",
                Description: "Rename, re-describe, or update settings on an owned dashboard. `settings` replaces the entire settings JSON. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "name": { "type": ["string", "null"] },
                        "description": { "type": ["string", "null"] },
                        "settings": { "type": ["object", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateDashboardAsync),

            new AgentTool(
                Name: "delete_dashboard",
                Description: "Delete an owned dashboard and every widget on it. Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteDashboardAsync),

            new AgentTool(
                Name: "add_widget",
                Description:
                    "Add a widget to an owned dashboard. `widgetType` matches a SPA registry key (see lookup-dashboards.list_widget_types). " +
                    "`config` is the widget's full Zod-validated body; defaults are applied client-side if omitted entirely. " +
                    "**The config blob is validated server-side BEFORE the proposal**: when dataSource.type is 'adHocAql' or 'savedQuery' the AQL gets parsed + type-checked, every column-mapping field (savedQueryLabelColumn, savedQueryValueColumn, bucketColumn, xAxisColumn, etc.) is checked to exist in the result schema, AND the query is probe-executed with hardCap=1 to confirm it actually runs (catches broken dataset sources, missing tables, permission errors). Validation failures return a ConfirmGate.Failed envelope with `parserErrors` (grammar), `availableColumns` (unknown column name), or `executionError` (runtime failure — usually means the dataset is misconfigured upstream) — fix and re-issue. " +
                    "On success the proposal includes `validatedSchema` so the user can see what data the chart will plot before approving. " +
                    "Grid coords default to (0,0,4,3) if not set. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dashboardId": { "type": "string" },
                        "widgetType": { "type": "string" },
                        "title": { "type": ["string", "null"] },
                        "config": { "type": ["object", "null"] },
                        "gridX": { "type": ["integer", "null"] },
                        "gridY": { "type": ["integer", "null"] },
                        "gridW": { "type": ["integer", "null"] },
                        "gridH": { "type": ["integer", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dashboardId", "widgetType"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeAddWidgetAsync),

            new AgentTool(
                Name: "update_widget",
                Description:
                    "Update an existing widget's title, config, or grid position. Any field omitted is left as-is. " +
                    "`config`, when provided, replaces the entire body (no deep-merge server-side) AND gets the same server-side validation as add_widget: " +
                    "AQL parse + type-check, plus column-existence checks for every column-mapping field. Validation runs against the widget's existing widgetType (which can't change via update). " +
                    "Failures return a ConfirmGate.Failed envelope with `parserErrors` or `availableColumns` — fix and re-issue. Title/grid-only updates skip validation. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dashboardId": { "type": "string" },
                        "widgetId": { "type": "string" },
                        "title": { "type": ["string", "null"] },
                        "config": { "type": ["object", "null"] },
                        "gridX": { "type": ["integer", "null"] },
                        "gridY": { "type": ["integer", "null"] },
                        "gridW": { "type": ["integer", "null"] },
                        "gridH": { "type": ["integer", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dashboardId", "widgetId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateWidgetAsync),

            new AgentTool(
                Name: "remove_widget",
                Description: "Remove a widget from an owned dashboard. Irreversible; confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dashboardId": { "type": "string" },
                        "widgetId": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dashboardId", "widgetId"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRemoveWidgetAsync),

            new AgentTool(
                Name: "reposition_widgets",
                Description: "Bulk-reposition widgets on an owned dashboard. `layout` is the full new layout for every widget the caller wants moved. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "dashboardId": { "type": "string" },
                        "layout": {
                          "type": "array",
                          "items": {
                            "type": "object",
                            "properties": {
                              "widgetId": { "type": "string" },
                              "gridX": { "type": "integer" },
                              "gridY": { "type": "integer" },
                              "gridW": { "type": "integer" },
                              "gridH": { "type": "integer" }
                            },
                            "required": ["widgetId", "gridX", "gridY", "gridW", "gridH"],
                            "additionalProperties": false
                          }
                        },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["dashboardId", "layout"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRepositionWidgetsAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Dashboards are owner-only in v1 — mutations on someone else's dashboard return NotFound. " +
        "WIDGET CONFIG WORKFLOW (do this every time): " +
        "(1) call lookup-dashboards.list_widget_types to see widget types, their TRUE configShape, and supportedSources. " +
        "(2) call lookup-dashboards.build_widget_config_template with widgetType + mode + binding args to get a complete, schema-valid `config` blob. DO NOT compose configs by hand — the schemas have non-obvious field names (savedQueryLabelColumn, recordGroupBy, etc.) and the SPA silently falls back to 'All records' if you pass an invalid dataSource shape. " +
        "(3) pass the returned config verbatim to add_widget / update_widget. " +
        "DATASET BINDING REMINDER: there is NO 'dataset' dataSource type. Use mode='adHocAql' and put real AQL in adHocAqlQuery. AQL is NOT SQL — clauses are `FROM <entity>[(arg)] [WHERE ...] [ORDER BY ...] [COLUMNS(<items>)] [GROUP(<fields>)] [LIMIT n]` in that exact order; use `COLUMNS(...)` not `SELECT`, `GROUP(...)` not `GROUP BY`, `LIMIT n` not `TAKE n`. Example: `FROM Dataset(\"name\") ORDER BY date COLUMNS(date, AVG(value) AS avg_value) GROUP(date)`. labelColumn (sets savedQueryLabelColumn) is the X axis / slice label; valueColumn (sets savedQueryValueColumn) is the Y axis / value. data-table does NOT support adHocAql — use a chart widget for dataset visualizations.";

    private static async Task<JsonElement> InvokeCreateDashboardAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "create_dashboard";
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        var description = ReadString(args, "description");
        var fromMountPath = ReadString(args, "fromMountPath");

        var preview = new { name = name.Trim(), description, fromMountPath };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_create_proposal", action, preview);

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        try
        {
            var row = await store.CreateAsync(
                new CreateDashboardInput(name.Trim(), description?.Trim(), fromMountPath),
                ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dashboard_create_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                source = row.Source,
                templateKey = row.TemplateKey
            });
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("dashboard_create_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUpdateDashboardAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "update_dashboard";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required.");
        var name = ReadString(args, "name");
        var description = ReadString(args, "description");
        JsonElement? settings = null;
        if (args.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
            settings = s.Clone();
        if (name is null && description is null && settings is null)
            return ConfirmGate.Rejected(action, "At least one of name, description, or settings must be set.");

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var existing = await store.GetForActorAsync(id, ctx.Session.UserId, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dashboard {id} not found or not owned by current user.");

        var preview = new
        {
            id,
            before = new { existing.Dashboard.Name, existing.Dashboard.Description },
            patch = new { name, description, settingsProvided = settings.HasValue }
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_update_proposal", action, preview);

        try
        {
            var row = await store.UpdateAsync(id,
                new UpdateDashboardInput(name?.Trim(), description?.Trim(), settings),
                ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dashboard_update_committed", action, new
            {
                id = row.Id,
                name = row.Name,
                description = row.Description
            });
        }
        catch (DashboardNotFoundException)
        {
            return ConfirmGate.Failed("dashboard_update_failed", action, $"Dashboard {id} not found or not owned by current user.");
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("dashboard_update_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeDeleteDashboardAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_dashboard";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required.");
        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var existing = await store.GetForActorAsync(id, ctx.Session.UserId, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dashboard {id} not found or not owned by current user.");

        var preview = new
        {
            id,
            existing.Dashboard.Name,
            widgetCount = existing.Widgets.Count,
            warning = "Irreversible. Every widget on the dashboard is removed."
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_delete_proposal", action, preview);

        var ok = await store.DeleteAsync(id, ctx.Session.UserId, ct);
        if (!ok) return ConfirmGate.Failed("dashboard_delete_failed", action, $"Dashboard {id} not found.");
        return ConfirmGate.Committed("dashboard_delete_committed", action, new { id, name = existing.Dashboard.Name });
    }

    private static async Task<JsonElement> InvokeAddWidgetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "add_widget";
        if (!TryReadGuid(args, "dashboardId", out var dashboardId))
            return ConfirmGate.Rejected(action, "dashboardId is required.");
        var widgetType = ReadString(args, "widgetType");
        if (string.IsNullOrWhiteSpace(widgetType))
            return ConfirmGate.Rejected(action, "widgetType is required.");
        var title = ReadString(args, "title");
        var configEl = ReadObject(args, "config");
        var gridX = ReadInt(args, "gridX") ?? 0;
        var gridY = ReadInt(args, "gridY") ?? 0;
        var gridW = ReadInt(args, "gridW") ?? 4;
        var gridH = ReadInt(args, "gridH") ?? 3;

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var existing = await store.GetForActorAsync(dashboardId, ctx.Session.UserId, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dashboard {dashboardId} not found or not owned by current user.");

        // Validate the config blob via the shared WidgetConfigValidator before
        // we ever offer a proposal. Same parser + column-existence checks
        // that build_widget_config_template runs — so a hand-composed config
        // gets the same gating as a template-produced one. On failure we
        // return a ConfirmGate.Failed envelope with the parser errors or the
        // result schema's available columns, so the agent can correct and
        // re-issue without the user ever seeing an approval prompt for a
        // widget that can't render.
        WidgetConfigValidator.Result? validation = null;
        if (configEl.HasValue)
        {
            validation = await WidgetConfigValidator.ValidateAsync(
                widgetType, configEl.Value, ctx, ct, executeProbe: true);
            if (!validation.Ok)
                return BuildValidationFailedEnvelope("dashboard_widget_add_failed", action, validation);
        }

        var preview = new
        {
            dashboardId,
            widgetType,
            title,
            grid = new { gridX, gridY, gridW, gridH },
            configProvided = configEl.HasValue,
            validatedSchema = validation?.ValidatedSchema?.Select(c => new
            {
                name = c.Name,
                dataType = c.DataType.ToString().ToLowerInvariant(),
                isAggregable = c.IsAggregable
            }).ToList()
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_widget_add_proposal", action, preview);

        try
        {
            var widget = await store.AddWidgetAsync(dashboardId,
                new CreateWidgetInput(widgetType, title, configEl ?? EmptyObject(), gridX, gridY, gridW, gridH),
                ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dashboard_widget_add_committed", action, new
            {
                widgetId = widget.Id,
                dashboardId = widget.DashboardId,
                widgetType = widget.WidgetType,
                title = widget.Title
            });
        }
        catch (DashboardNotFoundException)
        {
            return ConfirmGate.Failed("dashboard_widget_add_failed", action, $"Dashboard {dashboardId} not found or not owned by current user.");
        }
        catch (ArgumentException ex)
        {
            return ConfirmGate.Failed("dashboard_widget_add_failed", action, ex.Message);
        }
    }

    private static async Task<JsonElement> InvokeUpdateWidgetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "update_widget";
        if (!TryReadGuid(args, "dashboardId", out var dashboardId))
            return ConfirmGate.Rejected(action, "dashboardId is required.");
        if (!TryReadGuid(args, "widgetId", out var widgetId))
            return ConfirmGate.Rejected(action, "widgetId is required.");
        var title = ReadString(args, "title");
        var configEl = ReadObject(args, "config");
        var gridX = ReadInt(args, "gridX");
        var gridY = ReadInt(args, "gridY");
        var gridW = ReadInt(args, "gridW");
        var gridH = ReadInt(args, "gridH");
        if (title is null && configEl is null && gridX is null && gridY is null && gridW is null && gridH is null)
            return ConfirmGate.Rejected(action, "At least one of title, config, gridX, gridY, gridW, or gridH must be set.");

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var existing = await store.GetForActorAsync(dashboardId, ctx.Session.UserId, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dashboard {dashboardId} not found or not owned by current user.");
        var existingWidget = existing.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (existingWidget is null) return ConfirmGate.Rejected(action, $"Widget {widgetId} not found on dashboard {dashboardId}.");

        // Validate the replacement config (when provided) against the EXISTING
        // widget's type — clients can't change widgetType via update, so the
        // validator uses what's already in the row. Title/grid-only updates
        // skip validation entirely.
        WidgetConfigValidator.Result? validation = null;
        if (configEl.HasValue)
        {
            validation = await WidgetConfigValidator.ValidateAsync(
                existingWidget.WidgetType, configEl.Value, ctx, ct, executeProbe: true);
            if (!validation.Ok)
                return BuildValidationFailedEnvelope("dashboard_widget_update_failed", action, validation);
        }

        var preview = new
        {
            dashboardId,
            widgetId,
            widgetType = existingWidget.WidgetType,
            patch = new
            {
                title,
                configProvided = configEl.HasValue,
                grid = new { gridX, gridY, gridW, gridH }
            },
            validatedSchema = validation?.ValidatedSchema?.Select(c => new
            {
                name = c.Name,
                dataType = c.DataType.ToString().ToLowerInvariant(),
                isAggregable = c.IsAggregable
            }).ToList()
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_widget_update_proposal", action, preview);

        try
        {
            var widget = await store.UpdateWidgetAsync(dashboardId, widgetId,
                new UpdateWidgetInput(title, configEl, gridX, gridY, gridW, gridH),
                ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dashboard_widget_update_committed", action, new
            {
                widgetId = widget.Id,
                dashboardId = widget.DashboardId,
                widgetType = widget.WidgetType,
                title = widget.Title
            });
        }
        catch (DashboardNotFoundException)
        {
            return ConfirmGate.Failed("dashboard_widget_update_failed", action, $"Dashboard {dashboardId} not found or not owned by current user.");
        }
        catch (DashboardWidgetNotFoundException)
        {
            return ConfirmGate.Failed("dashboard_widget_update_failed", action, $"Widget {widgetId} not found.");
        }
    }

    private static async Task<JsonElement> InvokeRemoveWidgetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "remove_widget";
        if (!TryReadGuid(args, "dashboardId", out var dashboardId))
            return ConfirmGate.Rejected(action, "dashboardId is required.");
        if (!TryReadGuid(args, "widgetId", out var widgetId))
            return ConfirmGate.Rejected(action, "widgetId is required.");

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var existing = await store.GetForActorAsync(dashboardId, ctx.Session.UserId, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dashboard {dashboardId} not found or not owned by current user.");
        var existingWidget = existing.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (existingWidget is null) return ConfirmGate.Rejected(action, $"Widget {widgetId} not found on dashboard {dashboardId}.");

        var preview = new
        {
            dashboardId,
            widgetId,
            widgetType = existingWidget.WidgetType,
            title = existingWidget.Title,
            warning = "Irreversible."
        };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_widget_remove_proposal", action, preview);

        var ok = await store.RemoveWidgetAsync(dashboardId, widgetId, ctx.Session.UserId, ct);
        if (!ok) return ConfirmGate.Failed("dashboard_widget_remove_failed", action, $"Widget {widgetId} not found on dashboard {dashboardId}.");
        return ConfirmGate.Committed("dashboard_widget_remove_committed", action, new { dashboardId, widgetId });
    }

    private static async Task<JsonElement> InvokeRepositionWidgetsAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "reposition_widgets";
        if (!TryReadGuid(args, "dashboardId", out var dashboardId))
            return ConfirmGate.Rejected(action, "dashboardId is required.");
        if (!args.TryGetProperty("layout", out var layoutEl) || layoutEl.ValueKind != JsonValueKind.Array)
            return ConfirmGate.Rejected(action, "layout is required and must be an array.");

        var positions = new List<LayoutPosition>(layoutEl.GetArrayLength());
        foreach (var entry in layoutEl.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!TryReadGuid(entry, "widgetId", out var widgetId)) continue;
            var x = ReadInt(entry, "gridX") ?? 0;
            var y = ReadInt(entry, "gridY") ?? 0;
            var w = ReadInt(entry, "gridW") ?? 0;
            var h = ReadInt(entry, "gridH") ?? 0;
            positions.Add(new LayoutPosition(widgetId, x, y, w, h));
        }
        if (positions.Count == 0)
            return ConfirmGate.Rejected(action, "layout has no valid entries.");

        var store = ctx.Services.GetRequiredService<IDashboardStore>();
        var existing = await store.GetForActorAsync(dashboardId, ctx.Session.UserId, ct);
        if (existing is null) return ConfirmGate.Rejected(action, $"Dashboard {dashboardId} not found or not owned by current user.");

        var preview = new { dashboardId, positionCount = positions.Count };
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("dashboard_layout_proposal", action, preview);

        try
        {
            var updated = await store.ReplaceLayoutAsync(dashboardId, positions, ctx.Session.UserId, ct);
            return ConfirmGate.Committed("dashboard_layout_committed", action, new { dashboardId, updated });
        }
        catch (DashboardNotFoundException)
        {
            return ConfirmGate.Failed("dashboard_layout_failed", action, $"Dashboard {dashboardId} not found or not owned by current user.");
        }
    }

    // Format a WidgetConfigValidator.Result as a ConfirmGate.Failed envelope
    // with structured details. The Failed envelope is shaped identically to a
    // committed-but-rejected widget mutation — the agent sees `error` +
    // `details` and decides how to recover. The grammar hint is embedded
    // whenever the failure was a parse error so the agent has the clause-
    // order rule on-hand without re-querying.
    private static JsonElement BuildValidationFailedEnvelope(
        string kind, string action, WidgetConfigValidator.Result r)
    {
        object details;
        if (r.ParserErrors is not null)
        {
            details = new
            {
                sourceField = r.SourceField,
                queryText = r.QueryText,
                parserErrors = r.ParserErrors,
                grammarHint = WidgetConfigValidator.GrammarHint
            };
        }
        else if (r.ExecutionError is not null)
        {
            details = new
            {
                sourceField = r.SourceField,
                queryText = r.QueryText,
                executionError = r.ExecutionError,
                schemaSource = r.SchemaSource
            };
        }
        else
        {
            details = new
            {
                fieldName = r.SourceField,
                badValue = r.BadValue,
                schemaSource = r.SchemaSource,
                availableColumns = r.AvailableColumns?.Select(c => new
                {
                    name = c.Name,
                    dataType = c.DataType.ToString().ToLowerInvariant(),
                    isAggregable = c.IsAggregable
                }).ToList()
            };
        }
        return ConfirmGate.Failed(kind, action,
            r.Message ?? "Widget config failed validation.",
            details);
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

    private static int? ReadInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static JsonElement? ReadObject(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v.Clone()
            : null;

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
