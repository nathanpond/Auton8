using System.Text.RegularExpressions;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Apply a regex to a source column and write the (optional capture group's)
// match into a target column. Config:
//   column  = <source column>     (required)
//   pattern = <regex>             (required)
//   target  = <new column name>   (required)
//   group   = <capture group #>   (optional — default 0 = whole match)
public sealed class RegexExtractTransformer : ITransformer
{
    public string Key => "regex-extract";
    public string DisplayName => "Regex extract";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var column = DataFrameOps.ConfigValue(config, "column");
        var pattern = DataFrameOps.ConfigValue(config, "pattern");
        var target = DataFrameOps.ConfigValue(config, "target");
        var groupIndex = int.TryParse(DataFrameOps.OptionalConfig(config, "group"), out var g) ? g : 0;

        var regex = new Regex(pattern, RegexOptions.Compiled);
        var newColumns = input.Columns.ToList();
        if (input.FindColumn(target) is null)
        {
            newColumns.Add(new DataColumn(target, DataColumnType.Text));
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        foreach (var row in input.Rows)
        {
            var copy = new Dictionary<string, object?>(row, StringComparer.Ordinal);
            var src = DataFrameOps.AsString(DataFrameOps.RowValue(row, column));
            var match = regex.Match(src);
            copy[target] = match.Success && groupIndex < match.Groups.Count
                ? match.Groups[groupIndex].Value
                : null;
            rows.Add(copy);
        }
        return Task.FromResult(new DataFrame(newColumns, rows));
    }
}
