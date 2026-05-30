using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

public sealed class DistinctCountAnalyzer : IAnalyzer
{
    public string Key => "distinct-count";
    public string DisplayName => "Distinct count per column";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var columns = new[]
        {
            new DataColumn("column", DataColumnType.Text),
            new DataColumn("distinctCount", DataColumnType.Integer),
        };
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var col in input.Columns)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in input.Rows)
            {
                set.Add(DataFrameOps.AsString(DataFrameOps.RowValue(r, col.Name)));
            }
            rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["column"] = col.Name,
                ["distinctCount"] = (long)set.Count,
            });
        }
        return Task.FromResult(new DataFrame(columns, rows));
    }
}
