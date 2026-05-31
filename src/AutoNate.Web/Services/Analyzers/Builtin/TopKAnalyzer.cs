using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Top K most-frequent values in a column. Config:
//   column = <column name>   (required)
//   k      = <positive int>  (default 10)
public sealed class TopKAnalyzer : IAnalyzer
{
    public string Key => "top-k";
    public string DisplayName => "Top K values";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var column = DataFrameOps.ConfigValue(config, "column");
        var k = int.TryParse(DataFrameOps.OptionalConfig(config, "k"), out var parsed) && parsed > 0 ? parsed : 10;

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in input.Rows)
        {
            var v = DataFrameOps.AsString(DataFrameOps.RowValue(r, column));
            counts[v] = counts.TryGetValue(v, out var existing) ? existing + 1 : 1;
        }

        var columns = new[]
        {
            new DataColumn("value", DataColumnType.Text),
            new DataColumn("count", DataColumnType.Integer),
        };
        var rows = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(k)
            .Select(kv => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = kv.Key,
                ["count"] = kv.Value,
            })
            .ToList();
        return Task.FromResult(new DataFrame(columns, rows));
    }
}
