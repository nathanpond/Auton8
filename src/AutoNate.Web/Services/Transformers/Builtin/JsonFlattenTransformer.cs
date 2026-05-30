using System.Text.Json;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Flattens nested JSON values stored in a Json-typed column into new
// columns prefixed with the source column name. Config:
//   column    = <Json column to flatten>      (required)
//   separator = string between segments       (default ".")
//   maxDepth  = positive integer              (default 4)
public sealed class JsonFlattenTransformer : ITransformer
{
    public string Key => "json-flatten";
    public string DisplayName => "Flatten JSON column";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var column = DataFrameOps.ConfigValue(config, "column");
        var sep = DataFrameOps.OptionalConfig(config, "separator") ?? ".";
        var maxDepth = int.TryParse(DataFrameOps.OptionalConfig(config, "maxDepth"), out var d) && d > 0 ? d : 4;

        var newColumnNames = new List<string>();
        var newColumnSet = new HashSet<string>(StringComparer.Ordinal);
        var flattenedRows = new List<Dictionary<string, object?>>(input.Rows.Count);

        foreach (var row in input.Rows)
        {
            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in row)
            {
                if (string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase)) continue;
                copy[kv.Key] = kv.Value;
            }
            var rawJson = DataFrameOps.AsString(DataFrameOps.RowValue(row, column));
            if (!string.IsNullOrEmpty(rawJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    Flatten(doc.RootElement, column, sep, maxDepth, 0, copy, newColumnSet, newColumnNames);
                }
                catch (JsonException)
                {
                    // Leave the row as-is — the JSON didn't parse.
                }
            }
            flattenedRows.Add(copy);
        }

        var outColumns = input.Columns
            .Where(c => !string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase))
            .ToList();
        outColumns.AddRange(newColumnNames.Select(n => new DataColumn(n, DataColumnType.Text)));

        return Task.FromResult(new DataFrame(outColumns,
            flattenedRows.Cast<IReadOnlyDictionary<string, object?>>().ToList()));
    }

    private static void Flatten(
        JsonElement element,
        string prefix,
        string sep,
        int maxDepth,
        int depth,
        Dictionary<string, object?> row,
        HashSet<string> columnSet,
        List<string> columnOrder)
    {
        if (depth >= maxDepth || element.ValueKind != JsonValueKind.Object)
        {
            Emit(row, columnSet, columnOrder, prefix, element);
            return;
        }
        foreach (var prop in element.EnumerateObject())
        {
            var childKey = prefix + sep + prop.Name;
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(prop.Value, childKey, sep, maxDepth, depth + 1, row, columnSet, columnOrder);
            }
            else
            {
                Emit(row, columnSet, columnOrder, childKey, prop.Value);
            }
        }
    }

    private static void Emit(
        Dictionary<string, object?> row,
        HashSet<string> columnSet,
        List<string> columnOrder,
        string columnName,
        JsonElement value)
    {
        var rendered = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var l) ? (object)l : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText(),
        };
        row[columnName] = rendered;
        if (columnSet.Add(columnName)) columnOrder.Add(columnName);
    }
}
