using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Long → wide pivot. Config:
//   rowKey  = <column to keep as the row identity>
//   colKey  = <column whose distinct values become new columns>
//   valueKey = <column whose value populates the cells>
//   agg     = first | sum | mean   (default first)
public sealed class PivotTransformer : ITransformer
{
    public string Key => "pivot";
    public string DisplayName => "Pivot (long → wide)";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var rowKey = DataFrameOps.ConfigValue(config, "rowKey");
        var colKey = DataFrameOps.ConfigValue(config, "colKey");
        var valueKey = DataFrameOps.ConfigValue(config, "valueKey");
        var agg = (DataFrameOps.OptionalConfig(config, "agg") ?? "first").ToLowerInvariant();

        // Bucket: rowKey-value → (colKey-value → list of valueKey-values)
        var buckets = new Dictionary<string, Dictionary<string, List<double>>>(StringComparer.Ordinal);
        var firstValues = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var distinctColKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in input.Rows)
        {
            var rk = DataFrameOps.AsString(DataFrameOps.RowValue(row, rowKey));
            var ck = DataFrameOps.AsString(DataFrameOps.RowValue(row, colKey));
            var v = DataFrameOps.RowValue(row, valueKey);
            distinctColKeys.Add(ck);

            if (!buckets.TryGetValue(rk, out var inner))
            {
                inner = new Dictionary<string, List<double>>(StringComparer.Ordinal);
                buckets[rk] = inner;
                firstValues[rk] = new Dictionary<string, object?>(StringComparer.Ordinal);
            }
            if (!inner.TryGetValue(ck, out var list))
            {
                list = new List<double>();
                inner[ck] = list;
            }
            if (DataFrameOps.TryAsDouble(v, out var d))
            {
                list.Add(d);
            }
            if (!firstValues[rk].ContainsKey(ck))
            {
                firstValues[rk][ck] = v;
            }
        }

        var colKeyList = distinctColKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var outColumns = new List<DataColumn> { new(rowKey, DataColumnType.Text) };
        foreach (var ck in colKeyList) outColumns.Add(new DataColumn(ck, DataColumnType.Number));

        var rows = new List<IReadOnlyDictionary<string, object?>>(buckets.Count);
        foreach (var rk in buckets.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var rowOut = new Dictionary<string, object?>(StringComparer.Ordinal) { [rowKey] = rk };
            foreach (var ck in colKeyList)
            {
                if (!buckets[rk].TryGetValue(ck, out var values) || values.Count == 0)
                {
                    rowOut[ck] = firstValues[rk].TryGetValue(ck, out var fv) ? fv : null;
                    continue;
                }
                rowOut[ck] = agg switch
                {
                    "sum" => values.Sum(),
                    "mean" => values.Average(),
                    _ => firstValues[rk][ck],
                };
            }
            rows.Add(rowOut);
        }
        return Task.FromResult(new DataFrame(outColumns, rows));
    }
}
