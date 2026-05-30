using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// One row per numeric column with count/mean/min/max/p25/p50/p75.
// Boolean/text columns are silently skipped. No config knobs.
public sealed class SummaryStatisticsAnalyzer : IAnalyzer
{
    public string Key => "summary-statistics";
    public string DisplayName => "Summary statistics";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var columns = new[]
        {
            new DataColumn("column", DataColumnType.Text),
            new DataColumn("count", DataColumnType.Integer),
            new DataColumn("mean", DataColumnType.Number),
            new DataColumn("min", DataColumnType.Number),
            new DataColumn("max", DataColumnType.Number),
            new DataColumn("p25", DataColumnType.Number),
            new DataColumn("p50", DataColumnType.Number),
            new DataColumn("p75", DataColumnType.Number),
        };

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var col in input.Columns)
        {
            if (col.Type != DataColumnType.Integer && col.Type != DataColumnType.Number) continue;
            var values = input.Rows
                .Select(r => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, col.Name), out var d) ? d : (double?)null)
                .Where(v => v is not null)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();
            if (values.Count == 0)
            {
                rows.Add(EmptyRow(col.Name));
                continue;
            }
            rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["column"] = col.Name,
                ["count"] = (long)values.Count,
                ["mean"] = values.Average(),
                ["min"] = values[0],
                ["max"] = values[^1],
                ["p25"] = Quantile(values, 0.25),
                ["p50"] = Quantile(values, 0.50),
                ["p75"] = Quantile(values, 0.75),
            });
        }
        return Task.FromResult(new DataFrame(columns, rows));
    }

    private static IReadOnlyDictionary<string, object?> EmptyRow(string column) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["column"] = column,
            ["count"] = 0L,
            ["mean"] = null,
            ["min"] = null,
            ["max"] = null,
            ["p25"] = null,
            ["p50"] = null,
            ["p75"] = null,
        };

    private static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        var pos = q * (sorted.Count - 1);
        var lower = (int)Math.Floor(pos);
        var upper = (int)Math.Ceiling(pos);
        if (lower == upper) return sorted[lower];
        var weight = pos - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}
