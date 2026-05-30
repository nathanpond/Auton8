using System.Globalization;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Parse a text column into a Date column with explicit format support.
// Config:
//   column = <name>           (required)
//   format = <DateTime format> (optional — falls back to invariant DateTime.TryParse)
public sealed class DateNormalizeTransformer : ITransformer
{
    public string Key => "date-normalize";
    public string DisplayName => "Normalize a date column";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var column = DataFrameOps.ConfigValue(config, "column");
        var format = DataFrameOps.OptionalConfig(config, "format");

        var newColumns = input.Columns.Select(c =>
            string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)
                ? new DataColumn(c.Name, DataColumnType.Date)
                : c).ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        foreach (var row in input.Rows)
        {
            var copy = new Dictionary<string, object?>(row, StringComparer.Ordinal);
            var raw = DataFrameOps.RowValue(row, column);
            if (raw is not null)
            {
                copy[column] = NormalizeOne(raw, format) ?? raw;
            }
            rows.Add(copy);
        }
        return Task.FromResult(new DataFrame(newColumns, rows));
    }

    private static DateTime? NormalizeOne(object value, string? format)
    {
        if (value is DateTime dt) return dt;
        var raw = value.ToString();
        if (string.IsNullOrEmpty(raw)) return null;
        if (format is not null && DateTime.TryParseExact(raw, format, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose))
        {
            return loose;
        }
        return null;
    }
}
