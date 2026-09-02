namespace AutoNate.Web.Services.Transformers;

// Per-built-in config schemas (audit fix archived-7). The DisplayName mirrors the
// matching ITransformer / IAnalyzer's DisplayName so the schema endpoint
// can be self-describing without a registry round-trip on the SPA side.
//
// Field names match the runtime's `DataFrameOps.ConfigValue` / `OptionalConfig`
// reads in the corresponding *Transformer.cs / *Analyzer.cs file — if you
// rename a config key there, update the matching entry here or the
// editor's form will silently produce a no-op config.
//
// Plugin-contributed transformers don't appear here; the SPA falls back
// to its JSON Textarea when /api/transformers/{key}/schema returns 404.
public static class BuiltinSchemas
{
    private static ConfigFieldSchema Text(string name, string label, bool required = false,
        string? description = null, string? defaultValue = null, string? placeholder = null) =>
        new(name, label, "text", required, description, defaultValue, placeholder, null);

    private static ConfigFieldSchema Number(string name, string label, bool required = false,
        string? description = null, string? defaultValue = null) =>
        new(name, label, "number", required, description, defaultValue, null, null);

    private static ConfigFieldSchema Boolean(string name, string label,
        string? description = null, string? defaultValue = "true") =>
        new(name, label, "boolean", false, description, defaultValue, null, null);

    private static ConfigFieldSchema Select(string name, string label, string defaultValue,
        IReadOnlyList<string> options, string? description = null) =>
        new(name, label, "select", false, description, defaultValue, null, options);

    private static ConfigFieldSchema Columns(string name, string label, bool required = false,
        string? description = null, string? placeholder = "col1, col2") =>
        new(name, label, "columns", required, description, null, placeholder, null);

    public static readonly IReadOnlyDictionary<string, TransformerConfigSchema> Transformers =
        new Dictionary<string, TransformerConfigSchema>(StringComparer.Ordinal)
        {
            ["filter-rows"] = new("filter-rows", "Filter rows", new[]
            {
                Text("column", "Column", required: true, placeholder: "e.g. status"),
                Select("op", "Operator", "==",
                    new[] { "==", "!=", "<", "<=", ">", ">=", "contains" }),
                Text("value", "Value", required: true,
                    description: "String-coerced per column type."),
            }),

            ["dedupe"] = new("dedupe", "Deduplicate rows", new[]
            {
                Columns("columns", "Key columns",
                    description: "Comma-separated. Leave blank to dedupe on all columns."),
            }),

            ["join-two-inputs"] = new("join-two-inputs", "Join two inputs", new[]
            {
                Text("leftKey", "Left key column", required: true),
                Text("rightKey", "Right key column", required: true),
                Select("how", "Join type", "inner",
                    new[] { "inner", "left", "right", "outer" }),
            }),

            ["column-rename-cast"] = new("column-rename-cast", "Rename / cast a column", new[]
            {
                Text("from", "From column", required: true),
                Text("to", "To column",
                    description: "Defaults to `from` (cast-only rename)."),
                Select("type", "New column type", "text",
                    new[] { "text", "integer", "number", "boolean", "date", "json" }),
            }),

            ["null-fill"] = new("null-fill", "Fill nulls", new[]
            {
                Text("column", "Column", required: true),
                Select("strategy", "Strategy", "const",
                    new[] { "const", "previous" },
                    description: "`const` uses Value; `previous` carries the last non-null."),
                Text("value", "Const value",
                    description: "Used only when Strategy is `const`."),
            }),

            ["regex-extract"] = new("regex-extract", "Regex extract", new[]
            {
                Text("column", "Source column", required: true),
                Text("pattern", "Regex pattern", required: true,
                    placeholder: @"^(\d{4})-(\d{2})-(\d{2})"),
                Text("target", "Target column", required: true),
                Number("group", "Capture group index", description: "0 = whole match.",
                    defaultValue: "0"),
            }),

            ["json-flatten"] = new("json-flatten", "Flatten JSON column", new[]
            {
                Text("column", "JSON content column", required: true),
                Text("separator", "Path separator",
                    description: "Used between nested key names.", defaultValue: "."),
                Number("maxDepth", "Max depth", defaultValue: "4"),
            }),

            ["date-normalize"] = new("date-normalize", "Normalize a date column", new[]
            {
                Text("column", "Date column", required: true),
                Text("format", "Source format",
                    description: "Optional. Leave blank to auto-detect ISO 8601 / RFC 1123 forms."),
            }),

            ["pivot"] = new("pivot", "Pivot (long → wide)", new[]
            {
                Text("rowKey", "Row-key column", required: true),
                Text("colKey", "Column-key column", required: true,
                    description: "Distinct values become new columns."),
                Text("valueKey", "Value column", required: true),
                Select("agg", "Aggregation", "first",
                    new[] { "first", "sum", "avg", "min", "max", "count" }),
            }),

            ["unpivot"] = new("unpivot", "Unpivot (wide → long)", new[]
            {
                Columns("idColumns", "ID columns",
                    description: "Columns that stay as-is."),
                Columns("valueColumns", "Value columns",
                    description: "Columns to fold into name/value pairs."),
            }),

            ["csv-to-json"] = new("csv-to-json", "CSV → rows", new[]
            {
                Text("contentColumn", "CSV content column", defaultValue: "content"),
                Boolean("hasHeader", "First row is header"),
            }),

            ["json-to-csv"] = new("json-to-csv", "Rows → CSV", new[]
            {
                Boolean("includeHeader", "Include header row"),
            }),

            ["xlsx-to-csv"] = new("xlsx-to-csv", "XLSX → rows", new[]
            {
                Text("sheet", "Sheet name",
                    description: "Optional. Defaults to the first sheet."),
            }),

            ["schema-infer"] = new("schema-infer", "Infer schema",
                Array.Empty<ConfigFieldSchema>()),
        };

    public static readonly IReadOnlyDictionary<string, TransformerConfigSchema> Analyzers =
        new Dictionary<string, TransformerConfigSchema>(StringComparer.Ordinal)
        {
            ["summary-statistics"] = new("summary-statistics", "Summary statistics", new[]
            {
                Text("column", "Numeric column", required: true),
            }),

            ["top-k"] = new("top-k", "Top K values", new[]
            {
                Text("column", "Column", required: true),
                Number("k", "K", defaultValue: "10"),
            }),

            ["distinct-count"] = new("distinct-count", "Distinct count per column",
                Array.Empty<ConfigFieldSchema>()),

            ["null-rate"] = new("null-rate", "Null rate per column",
                Array.Empty<ConfigFieldSchema>()),

            ["group-by-aggregate"] = new("group-by-aggregate", "Group by + aggregate", new[]
            {
                Columns("groupBy", "Group-by columns", required: true),
                Text("agg", "Aggregations", required: true,
                    description: "Comma-separated specs like `revenue:sum, qty:avg`. Funcs: sum/avg/min/max/count/first.",
                    placeholder: "revenue:sum, qty:avg"),
            }),

            ["histogram-bin"] = new("histogram-bin", "Histogram (equal-width bins)", new[]
            {
                Text("column", "Numeric column", required: true),
                Number("bins", "Bin count", defaultValue: "10"),
            }),

            ["k-means-cluster"] = new("k-means-cluster", "K-means clustering", new[]
            {
                Columns("columns", "Feature columns", required: true),
                Number("k", "Cluster count (k)", defaultValue: "3"),
                Number("maxIterations", "Max iterations", defaultValue: "50"),
            }),

            ["anomaly-zscore"] = new("anomaly-zscore", "Anomaly detection (z-score)", new[]
            {
                Text("column", "Numeric column", required: true),
                Number("threshold", "z-score threshold", defaultValue: "3.0"),
            }),

            ["anomaly-iqr"] = new("anomaly-iqr", "Anomaly detection (IQR fence)", new[]
            {
                Text("column", "Numeric column", required: true),
                Number("factor", "IQR factor", defaultValue: "1.5"),
            }),

            ["correlation-matrix"] = new("correlation-matrix", "Pearson correlation matrix", new[]
            {
                Columns("columns", "Numeric columns", required: true),
            }),

            ["trend-linear-regression"] = new("trend-linear-regression", "Linear regression", new[]
            {
                Text("x", "Independent variable column", required: true),
                Text("y", "Dependent variable column", required: true),
            }),
        };
}
