using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Replace null values in a column with either a constant or a directional
// neighbor. Config:
//   column   = <name>                       (required)
//   strategy = const | forward | back       (default const)
//   value    = <literal>                    (required when strategy=const)
public sealed class NullFillTransformer : ITransformer
{
    public string Key => "null-fill";
    public string DisplayName => "Fill nulls";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var column = DataFrameOps.ConfigValue(config, "column");
        var strategy = (DataFrameOps.OptionalConfig(config, "strategy") ?? "const").ToLowerInvariant();
        var constValue = DataFrameOps.OptionalConfig(config, "value");

        var rows = input.Rows.Select(r => new Dictionary<string, object?>(r, StringComparer.Ordinal)).ToList();

        if (strategy == "const")
        {
            if (constValue is null)
                throw new InvalidOperationException("null-fill with strategy=const requires `value`.");
            foreach (var r in rows)
            {
                if (!HasValue(r, column))
                {
                    r[column] = constValue;
                }
            }
        }
        else if (strategy == "forward")
        {
            object? last = null;
            foreach (var r in rows)
            {
                if (HasValue(r, column)) { last = DataFrameOps.RowValue(r, column); }
                else if (last is not null) { r[column] = last; }
            }
        }
        else if (strategy == "back")
        {
            object? next = null;
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                if (HasValue(rows[i], column)) { next = DataFrameOps.RowValue(rows[i], column); }
                else if (next is not null) { rows[i][column] = next; }
            }
        }
        return Task.FromResult(new DataFrame(input.Columns, rows.Cast<IReadOnlyDictionary<string, object?>>().ToList()));
    }

    private static bool HasValue(IReadOnlyDictionary<string, object?> row, string column)
    {
        var v = DataFrameOps.RowValue(row, column);
        return v is not null && !string.IsNullOrEmpty(v as string);
    }
}
