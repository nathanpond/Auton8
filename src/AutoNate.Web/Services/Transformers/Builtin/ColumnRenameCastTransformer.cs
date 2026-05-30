using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Single-column rename and/or recast. Config:
//   from = <existing column name>  (required)
//   to   = <new name>              (optional — defaults to `from`)
//   type = text|integer|number|boolean|date|json  (optional — keeps existing)
public sealed class ColumnRenameCastTransformer : ITransformer
{
    public string Key => "column-rename-cast";
    public string DisplayName => "Rename / cast a column";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var from = DataFrameOps.ConfigValue(config, "from");
        var to = DataFrameOps.OptionalConfig(config, "to") ?? from;
        var typeStr = DataFrameOps.OptionalConfig(config, "type");

        var fromColumn = input.FindColumn(from);
        if (fromColumn is null)
        {
            throw new InvalidOperationException($"Column '{from}' not present in input.");
        }
        var newType = typeStr is null ? fromColumn.Type : ParseType(typeStr);
        var newColumns = input.Columns.Select(c =>
            string.Equals(c.Name, from, StringComparison.OrdinalIgnoreCase)
                ? new DataColumn(to, newType)
                : c).ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        foreach (var row in input.Rows)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in row)
            {
                if (string.Equals(kv.Key, from, StringComparison.OrdinalIgnoreCase))
                {
                    copy[to] = CoerceValue(kv.Value, newType);
                }
                else
                {
                    copy[kv.Key] = kv.Value;
                }
            }
            rows.Add(copy);
        }
        return Task.FromResult(new DataFrame(newColumns, rows));
    }

    private static DataColumnType ParseType(string raw) => raw.ToLowerInvariant() switch
    {
        "text" or "string" => DataColumnType.Text,
        "integer" or "int" or "long" or "bigint" => DataColumnType.Integer,
        "number" or "double" or "float" => DataColumnType.Number,
        "boolean" or "bool" => DataColumnType.Boolean,
        "date" or "datetime" or "timestamp" or "timestamptz" => DataColumnType.Date,
        "json" => DataColumnType.Json,
        _ => throw new InvalidOperationException($"Unknown column type '{raw}'."),
    };

    private static object? CoerceValue(object? value, DataColumnType target)
    {
        if (value is null) return null;
        return target switch
        {
            DataColumnType.Integer => DataFrameOps.TryAsDouble(value, out var d) ? (long)d : value,
            DataColumnType.Number => DataFrameOps.TryAsDouble(value, out var d) ? d : value,
            DataColumnType.Boolean => bool.TryParse(value.ToString(), out var b) ? b : value,
            DataColumnType.Date => DataFrameOps.TryAsDateTime(value, out var dt) ? dt : value,
            _ => DataFrameOps.AsString(value),
        };
    }
}
