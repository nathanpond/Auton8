using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Keep the first row per distinct (col1, col2, ...) key. Config:
//   columns = comma-separated column names (default: all columns)
public sealed class DedupeTransformer : ITransformer
{
    public string Key => "dedupe";
    public string DisplayName => "Deduplicate rows";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var keyCols = DataFrameOps.SplitColumnList(DataFrameOps.OptionalConfig(config, "columns"));
        var effectiveCols = keyCols.Count > 0 ? keyCols : input.Columns.Select(c => c.Name).ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        foreach (var row in input.Rows)
        {
            var key = string.Join("\u0001",
                effectiveCols.Select(c => DataFrameOps.AsString(DataFrameOps.RowValue(row, c))));
            if (seen.Add(key))
            {
                rows.Add(row);
            }
        }
        return Task.FromResult(new DataFrame(input.Columns, rows));
    }
}
