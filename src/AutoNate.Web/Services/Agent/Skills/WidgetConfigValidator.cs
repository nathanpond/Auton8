using System.Text.Json;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Cross-skill validation for dashboard widget configs. Both the build-
// template path in LookupDashboardsSkill and the add/update paths in
// ManageDashboardsSkill call this so a config blob receives the SAME
// parse + column-existence checks regardless of how it was composed
// (template-tool output or hand-typed). Single source of truth keeps the
// two skills from drifting out of sync.
internal static class WidgetConfigValidator
{
    // Discriminated outcome from a validation pass. `ParserErrors` is set
    // when the AQL didn't parse / type-check; `AvailableColumns` is set
    // when the AQL parsed but a column-mapping field referenced a column
    // that doesn't exist in the result schema; `ExecutionError` is set when
    // the query parsed and column-mapped cleanly but blew up at runtime
    // (broken dataset source, missing underlying table, permission error,
    // etc.). All-null + Ok=true means the config passed. `ValidatedSchema`
    // is populated on success (when the config used adHocAql / savedQuery)
    // so callers can surface the result column list to the agent for
    // downstream styling decisions.
    public sealed record Result(
        bool Ok,
        string? Message,
        string? SourceField,
        string? BadValue,
        string? QueryText,
        IReadOnlyList<string>? ParserErrors,
        string? SchemaSource,
        IReadOnlyList<QueryColumn>? AvailableColumns,
        IReadOnlyList<QueryColumn>? ValidatedSchema,
        string? ExecutionError = null)
    {
        public static Result Pass(IReadOnlyList<QueryColumn>? schema) =>
            new(true, null, null, null, null, null, null, null, schema);
    }

    // executeProbe=true triggers a hardCap=1 test execution after the parse +
    // column-mapping checks pass. add_widget / update_widget opt in so a
    // widget can't ship with a query that parses but blows up at runtime;
    // build_widget_config_template stays probe-off because planning iterates
    // many configs and the cost adds up.
    public static async Task<Result> ValidateAsync(
        string widgetType,
        JsonElement config,
        AgentToolContext ctx,
        CancellationToken ct,
        bool executeProbe = false)
    {
        if (!TryReadDataSourceType(config, out var type))
        {
            return new Result(false,
                "config.dataSource.type is missing or not one of 'records' | 'workflows' | 'savedQuery' | 'adHocAql'.",
                "dataSource.type", null, null, null, null, null, null);
        }

        // Step 1 — parse + type-check the AQL when the config uses an
        // AQL-backed source. For savedQuery mode we resolve the row first
        // and validate its persisted queryText; the result is the same shape
        // either way, which keeps the column-existence pass below uniform.
        IReadOnlyList<QueryColumn>? validatedSchema = null;
        string? schemaSource = null;
        string? probeQueryText = null;
        if (type == "adHocAql")
        {
            var query = ReadDataSourceString(config, "adHocAqlQuery") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return new Result(false,
                    "config.dataSource.adHocAqlQuery is empty for dataSource.type='adHocAql'.",
                    "dataSource.adHocAqlQuery", null, query, null, null, null, null);
            }
            var (ok, errors, schema) = await ValidateAqlAsync(query, ctx, ct);
            if (!ok)
            {
                return new Result(false,
                    "config.dataSource.adHocAqlQuery failed AQL parse / type-check.",
                    "dataSource.adHocAqlQuery", null, query, errors, null, null, null);
            }
            validatedSchema = schema;
            schemaSource = "adHocAqlQuery";
            probeQueryText = query;
        }
        else if (type == "savedQuery")
        {
            var savedQueryIdStr = ReadDataSourceString(config, "savedQueryId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedQueryIdStr) || !Guid.TryParse(savedQueryIdStr, out var sqGuid))
            {
                return new Result(false,
                    "config.dataSource.savedQueryId is missing or not a GUID for dataSource.type='savedQuery'.",
                    "dataSource.savedQueryId", savedQueryIdStr, null, null, null, null, null);
            }
            var sqStore = ctx.Services.GetRequiredService<ISavedQueryStore>();
            var sq = await sqStore.GetForActorAsync(sqGuid, ctx.Session.UserId, ct);
            if (sq is null)
            {
                return new Result(false,
                    $"Saved query {savedQueryIdStr} not found, not shared, or not owned by current user.",
                    "dataSource.savedQueryId", savedQueryIdStr, null, null, null, null, null);
            }
            var (ok, errors, schema) = await ValidateAqlAsync(sq.QueryText, ctx, ct);
            if (!ok)
            {
                return new Result(false,
                    $"Saved query '{sq.Name}' has invalid AQL — chart cannot render until the saved query is fixed.",
                    $"savedQuery '{sq.Name}' (queryText)", null, sq.QueryText, errors, null, null, null);
            }
            validatedSchema = schema;
            schemaSource = $"savedQuery '{sq.Name}'";
            probeQueryText = sq.QueryText;
        }

        // Step 2 — when we have a result schema, verify every column-mapping
        // field on the config points at a column the AQL actually produces.
        // Per-widget rules: mantine-chart family uses savedQueryLabelColumn /
        // savedQueryValueColumn; composite uses bucketColumn + per-series
        // valueColumn; quadrant family uses xAxis/yAxis/size/label/category.
        // Records / workflows modes skip these because column resolution is
        // record-type-specific and happens at runtime in the SPA.
        if (validatedSchema is not null)
        {
            if (IsMantineChart(widgetType))
            {
                var label = ReadConfigString(config, "savedQueryLabelColumn");
                if (!string.IsNullOrEmpty(label) && !ColumnExists(label, validatedSchema))
                    return MakeColumnNotFound("savedQueryLabelColumn", label, schemaSource!, validatedSchema);
                var value = ReadConfigString(config, "savedQueryValueColumn");
                if (!string.IsNullOrEmpty(value) && !ColumnExists(value, validatedSchema))
                    return MakeColumnNotFound("savedQueryValueColumn", value, schemaSource!, validatedSchema);
            }
            else if (widgetType == "chart-composite")
            {
                var bucket = ReadConfigString(config, "bucketColumn");
                if (!string.IsNullOrEmpty(bucket) && !ColumnExists(bucket, validatedSchema))
                    return MakeColumnNotFound("bucketColumn", bucket, schemaSource!, validatedSchema);
                if (config.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    foreach (var entry in series.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.Object
                            && entry.TryGetProperty("valueColumn", out var vc)
                            && vc.ValueKind == JsonValueKind.String
                            && vc.GetString() is { Length: > 0 } vcStr
                            && !ColumnExists(vcStr, validatedSchema))
                        {
                            return MakeColumnNotFound($"series[{i}].valueColumn", vcStr, schemaSource!, validatedSchema);
                        }
                        i++;
                    }
                }
            }
            else if (widgetType is "chart-quadrant" or "chart-bubble" or "chart-scatter")
            {
                var x = ReadConfigString(config, "xAxisColumn");
                if (!string.IsNullOrEmpty(x) && !ColumnExists(x, validatedSchema))
                    return MakeColumnNotFound("xAxisColumn", x, schemaSource!, validatedSchema);
                var y = ReadConfigString(config, "yAxisColumn");
                if (!string.IsNullOrEmpty(y) && !ColumnExists(y, validatedSchema))
                    return MakeColumnNotFound("yAxisColumn", y, schemaSource!, validatedSchema);
                var size = ReadConfigString(config, "sizeColumn");
                if (!string.IsNullOrEmpty(size) && !ColumnExists(size, validatedSchema))
                    return MakeColumnNotFound("sizeColumn", size, schemaSource!, validatedSchema);
                var label = ReadConfigString(config, "labelColumn");
                if (!string.IsNullOrEmpty(label) && !ColumnExists(label, validatedSchema))
                    return MakeColumnNotFound("labelColumn", label, schemaSource!, validatedSchema);
                var category = ReadConfigString(config, "categoryColumn");
                if (!string.IsNullOrEmpty(category) && !ColumnExists(category, validatedSchema))
                    return MakeColumnNotFound("categoryColumn", category, schemaSource!, validatedSchema);
            }
            // data-table: no column-mapping fields — its dataSource.type was
            // already gated to records|workflows by SupportedSources upstream.
        }

        // Step 3 — opt-in probe execution. Parse + column-mapping prove the
        // query is well-formed against the dataset's *declared* schema, but
        // a dataset can be declared correctly yet point at a non-existent
        // source (fabricated sourceId, missing table, hand-edited row). The
        // probe runs the actual query with hardCap=1; any runtime failure
        // (Postgres error, dataset source not resolvable, permission denied)
        // surfaces here instead of leaving the widget broken on the page.
        if (executeProbe && probeQueryText is not null)
        {
            var executor = ctx.Services.GetRequiredService<IAqlExecutor>();
            try
            {
                await executor.ExecuteAsync(probeQueryText, ctx.Session.User, hardCap: 1, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new Result(false,
                    "Widget AQL parses cleanly but failed when executed (hardCap=1 probe). The widget would render an error on the dashboard. Fix the underlying issue (dataset source, table, permissions) before re-issuing.",
                    "dataSource.adHocAqlQuery", null, probeQueryText, null, schemaSource, null, validatedSchema,
                    ExecutionError: ex.Message);
            }
        }

        return Result.Pass(validatedSchema);
    }

    // Standard grammar-correction hint embedded in every failure envelope so
    // the agent doesn't have to recall the rules from elsewhere.
    public const string GrammarHint =
        "AQL clauses must appear in this exact order: FROM → WHERE → ORDER BY → COLUMNS → GROUP → LIMIT. " +
        "AQL is NOT SQL — uses COLUMNS(...) not SELECT, GROUP(...) not GROUP BY, LIMIT n not TAKE n. Aliases use AS. " +
        "On Dataset queries (the chart-binding path) ORDER BY can reference a COLUMNS alias directly — " +
        "`FROM Dataset(\"name\") ORDER BY avg_value DESC COLUMNS(date, AVG(value) AS avg_value) GROUP(date)` works. " +
        "On other entities (Records, Flows, Notes, Workflow*) ORDER BY resolves only against the source schema; " +
        "repeat the expression instead: `ORDER BY AVG(value) DESC ...`.";

    private static bool IsMantineChart(string widgetType) =>
        widgetType is "chart-bar" or "chart-line" or "chart-area" or "chart-donut"
            or "chart-pie" or "chart-radial-bar" or "chart-funnel" or "chart-bars-list"
            or "chart-treemap" or "mantine-chart";

    private static bool TryReadDataSourceType(JsonElement config, out string type)
    {
        type = string.Empty;
        if (!config.TryGetProperty("dataSource", out var ds) || ds.ValueKind != JsonValueKind.Object) return false;
        if (!ds.TryGetProperty("type", out var tv) || tv.ValueKind != JsonValueKind.String) return false;
        var t = tv.GetString();
        if (t is "records" or "workflows" or "savedQuery" or "adHocAql") { type = t; return true; }
        return false;
    }

    private static string? ReadDataSourceString(JsonElement config, string name) =>
        config.TryGetProperty("dataSource", out var ds) && ds.ValueKind == JsonValueKind.Object
            && ds.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

    private static string? ReadConfigString(JsonElement config, string name) =>
        config.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool ColumnExists(string name, IReadOnlyList<QueryColumn> schema) =>
        schema.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Result MakeColumnNotFound(
        string fieldName, string badValue, string schemaSource, IReadOnlyList<QueryColumn> schema) =>
        new(false,
            $"config.{fieldName} '{badValue}' is not in the result schema of {schemaSource}. Use a column name from `availableColumns`.",
            fieldName, badValue, null, null, schemaSource, schema, null);

    // Parse + type-check an AQL query (same path as aql-assist.parse_aql:
    // AqlParser → AqlValidator), then derive the post-projection result
    // schema. We can't use prepared.Schema directly — it's the SOURCE
    // entity's static schema (every dataset column / every record field),
    // which is correct for "what can I query" introspection but wrong for
    // widget binding. Widgets reference projected column names like
    // "avg_temp", which exist only after the COLUMNS(...) clause runs.
    // AqlResultSchema.Derive(ast, prepared.Schema) closes that gap.
    private static async Task<(bool ok, IReadOnlyList<string> errors, IReadOnlyList<QueryColumn> schema)> ValidateAqlAsync(
        string queryText, AgentToolContext ctx, CancellationToken ct)
    {
        try
        {
            var ast = AqlParser.Parse(queryText);
            var registry = ctx.Services.GetRequiredService<IQueryEntityRegistry>();
            var validator = new AqlValidator(registry);
            var prepared = await validator.ValidateAsync(ast, hardCap: 1000, ct);
            var resultSchema = AqlResultSchema.Derive(ast, prepared.Schema);
            return (prepared.ValidationErrors.Count == 0, prepared.ValidationErrors, resultSchema);
        }
        catch (AqlValidationException ex)
        {
            return (false, ex.Errors.ToList(), Array.Empty<QueryColumn>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, new[] { "Parse error: " + ex.Message }, Array.Empty<QueryColumn>());
        }
    }
}
