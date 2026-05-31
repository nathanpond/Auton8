using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Flags rows whose `column` value falls outside [Q1 - k*IQR, Q3 + k*IQR].
// Config:
//   column = <numeric column>   (required)
//   factor = <double>           (default 1.5 — the canonical Tukey fence)
public sealed class AnomalyIqrAnalyzer : IAnalyzer
{
    public string Key => "anomaly-iqr";
    public string DisplayName => "Anomaly detection (IQR fence)";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var column = DataFrameOps.ConfigValue(config, "column");
        var factor = double.TryParse(DataFrameOps.OptionalConfig(config, "factor"), out var f) ? f : 1.5;

        var values = input.Rows
            .Select(r => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, column), out var d) ? d : (double?)null)
            .ToList();
        var sortedPresent = values.Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
        var q1 = Quantile(sortedPresent, 0.25);
        var q3 = Quantile(sortedPresent, 0.75);
        var iqr = q3 - q1;
        var lo = q1 - factor * iqr;
        var hi = q3 + factor * iqr;

        var newColumns = input.Columns.ToList();
        newColumns.Add(new DataColumn("isAnomaly", DataColumnType.Boolean));

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        for (var i = 0; i < input.Rows.Count; i++)
        {
            var copy = new Dictionary<string, object?>(input.Rows[i], StringComparer.Ordinal);
            var v = values[i];
            copy["isAnomaly"] = v is not null && (v.Value < lo || v.Value > hi);
            rows.Add(copy);
        }
        return Task.FromResult(new DataFrame(newColumns, rows));
    }

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
