using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Group-by with one or more aggregate measures. Config:
//   groupBy = comma-separated dimension columns
//   agg     = comma-separated "fn:column" pairs (e.g. "sum:Amount,avg:Tax,count:*")
//             supported fns: count, sum, avg, min, max
public sealed class GroupByAggregateAnalyzer : IAnalyzer
{
    public string Key => "group-by-aggregate";
    public string DisplayName => "Group by + aggregate";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var groupBy = DataFrameOps.SplitColumnList(DataFrameOps.ConfigValue(config, "groupBy"));
        var aggSpecs = DataFrameOps.SplitColumnList(DataFrameOps.ConfigValue(config, "agg"))
            .Select(ParseAggSpec)
            .ToList();

        var groups = new Dictionary<string, (string[] Key, List<IReadOnlyDictionary<string, object?>> Rows)>(
            StringComparer.Ordinal);
        foreach (var r in input.Rows)
        {
            var key = groupBy.Select(c => DataFrameOps.AsString(DataFrameOps.RowValue(r, c))).ToArray();
            var keyStr = string.Join("\u0001", key);
            if (!groups.TryGetValue(keyStr, out var bucket))
            {
                bucket = (key, new List<IReadOnlyDictionary<string, object?>>());
                groups[keyStr] = bucket;
            }
            bucket.Rows.Add(r);
        }

        var columns = new List<DataColumn>();
        foreach (var g in groupBy)
        {
            var src = input.FindColumn(g);
            columns.Add(new DataColumn(g, src?.Type ?? DataColumnType.Text));
        }
        foreach (var (fn, col) in aggSpecs)
        {
            columns.Add(new DataColumn($"{fn}_{col}",
                fn == "count" ? DataColumnType.Integer : DataColumnType.Number));
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(groups.Count);
        foreach (var bucket in groups.Values)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < groupBy.Count; i++) row[groupBy[i]] = bucket.Key[i];
            foreach (var (fn, col) in aggSpecs)
            {
                row[$"{fn}_{col}"] = Aggregate(fn, col, bucket.Rows);
            }
            rows.Add(row);
        }
        return Task.FromResult(new DataFrame(columns, rows));
    }

    private static (string Fn, string Col) ParseAggSpec(string spec)
    {
        var parts = spec.Split(':', 2);
        if (parts.Length != 2)
            throw new InvalidOperationException($"Aggregate spec '{spec}' must be 'fn:column'.");
        return (parts[0].Trim().ToLowerInvariant(), parts[1].Trim());
    }

    private static object? Aggregate(string fn, string col, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (fn == "count") return (long)rows.Count;
        var values = rows
            .Select(r => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, col), out var d) ? (double?)d : null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
        if (values.Count == 0) return null;
        return fn switch
        {
            "sum" => values.Sum(),
            "avg" or "mean" => values.Average(),
            "min" => values.Min(),
            "max" => values.Max(),
            _ => throw new InvalidOperationException($"Unknown aggregate function '{fn}'."),
        };
    }
}
