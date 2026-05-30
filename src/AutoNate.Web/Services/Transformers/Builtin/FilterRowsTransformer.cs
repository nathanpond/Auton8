using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Drops rows that don't match a single (column op value) predicate. Config:
//   column = <name>      (required)
//   op     = == | != | < | <= | > | >= | contains   (default ==)
//   value  = <literal>   (required, string-coerced per column type)
// AND-of-multiple-predicates lands when the pipeline editor models a chain;
// for now compose two FilterRows nodes in series.
public sealed class FilterRowsTransformer : ITransformer
{
    public string Key => "filter-rows";
    public string DisplayName => "Filter rows";
    public int InputArity => 1;

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var column = DataFrameOps.ConfigValue(config, "column");
        var op = DataFrameOps.OptionalConfig(config, "op") ?? "==";
        var value = DataFrameOps.ConfigValue(config, "value");

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        foreach (var row in input.Rows)
        {
            if (Match(DataFrameOps.RowValue(row, column), op, value))
            {
                rows.Add(row);
            }
        }
        return Task.FromResult(new DataFrame(input.Columns, rows));
    }

    private static bool Match(object? actual, string op, string expected)
    {
        switch (op)
        {
            case "==" or "=":
                return string.Equals(DataFrameOps.AsString(actual), expected, StringComparison.Ordinal);
            case "!=" or "<>":
                return !string.Equals(DataFrameOps.AsString(actual), expected, StringComparison.Ordinal);
            case "contains":
                return DataFrameOps.AsString(actual).Contains(expected, StringComparison.OrdinalIgnoreCase);
            case "<" or "<=" or ">" or ">=":
                if (!DataFrameOps.TryAsDouble(actual, out var a)) return false;
                if (!DataFrameOps.TryAsDouble(expected, out var e)) return false;
                return op switch
                {
                    "<" => a < e,
                    "<=" => a <= e,
                    ">" => a > e,
                    ">=" => a >= e,
                    _ => false,
                };
            default:
                return false;
        }
    }
}
