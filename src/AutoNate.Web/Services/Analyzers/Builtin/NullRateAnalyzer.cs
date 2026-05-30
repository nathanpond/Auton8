using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// One row per column with null count + rate.
public sealed class NullRateAnalyzer : IAnalyzer
{
    public string Key => "null-rate";
    public string DisplayName => "Null rate per column";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var columns = new[]
        {
            new DataColumn("column", DataColumnType.Text),
            new DataColumn("nullCount", DataColumnType.Integer),
            new DataColumn("nullRate", DataColumnType.Number),
            new DataColumn("totalRows", DataColumnType.Integer),
        };
        var total = input.Rows.Count;
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var col in input.Columns)
        {
            var nullCount = input.Rows.Count(r =>
            {
                var v = DataFrameOps.RowValue(r, col.Name);
                return v is null || (v is string s && string.IsNullOrEmpty(s));
            });
            rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["column"] = col.Name,
                ["nullCount"] = (long)nullCount,
                ["nullRate"] = total == 0 ? 0.0 : (double)nullCount / total,
                ["totalRows"] = (long)total,
            });
        }
        return Task.FromResult(new DataFrame(columns, rows));
    }
}
