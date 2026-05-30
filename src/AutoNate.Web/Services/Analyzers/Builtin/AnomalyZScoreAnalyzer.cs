using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Flags rows whose value in `column` has |z-score| > threshold. Config:
//   column    = <numeric column>   (required)
//   threshold = <double>           (default 3.0)
// Output preserves original columns and adds `zscore` + `isAnomaly`.
public sealed class AnomalyZScoreAnalyzer : IAnalyzer
{
    public string Key => "anomaly-zscore";
    public string DisplayName => "Anomaly detection (z-score)";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var column = DataFrameOps.ConfigValue(config, "column");
        var threshold = double.TryParse(DataFrameOps.OptionalConfig(config, "threshold"), out var t) ? t : 3.0;

        var values = input.Rows
            .Select(r => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, column), out var d) ? (double?)d : null)
            .ToList();
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        var mean = present.Count == 0 ? 0 : present.Average();
        var stdDev = present.Count < 2
            ? 0
            : Math.Sqrt(present.Sum(v => (v - mean) * (v - mean)) / (present.Count - 1));

        var newColumns = input.Columns.ToList();
        newColumns.Add(new DataColumn("zscore", DataColumnType.Number));
        newColumns.Add(new DataColumn("isAnomaly", DataColumnType.Boolean));

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        for (var i = 0; i < input.Rows.Count; i++)
        {
            var copy = new Dictionary<string, object?>(input.Rows[i], StringComparer.Ordinal);
            var v = values[i];
            if (v is null || stdDev <= double.Epsilon)
            {
                copy["zscore"] = null;
                copy["isAnomaly"] = false;
            }
            else
            {
                var z = (v.Value - mean) / stdDev;
                copy["zscore"] = z;
                copy["isAnomaly"] = Math.Abs(z) > threshold;
            }
            rows.Add(copy);
        }
        return Task.FromResult(new DataFrame(newColumns, rows));
    }
}
