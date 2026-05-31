using System.Globalization;
using AutoNate.Plugins.Abstractions;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Parses a CSV blob held in the upstream frame's first row's `content`
// column into rows. Used after a Files-type DataConnector / DataStore
// hands a CSV file's text contents to the pipeline. Config:
//   contentColumn = <input column holding the CSV text>  (default "content")
//   hasHeader     = true | false                          (default true)
public sealed class CsvToJsonTransformer : ITransformer
{
    public string Key => "csv-to-json";
    public string DisplayName => "CSV → rows";

    public async Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0 || inputs[0].Rows.Count == 0) return DataFrame.Empty;
        var input = inputs[0];
        var contentColumn = DataFrameOps.OptionalConfig(config, "contentColumn") ?? "content";
        var hasHeader = !string.Equals(DataFrameOps.OptionalConfig(config, "hasHeader"), "false",
            StringComparison.OrdinalIgnoreCase);

        var raw = DataFrameOps.AsString(DataFrameOps.RowValue(input.Rows[0], contentColumn));
        if (raw.Length == 0) return DataFrame.Empty;

        using var reader = new StringReader(raw);
        using var parser = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeader,
            BadDataFound = null,
            MissingFieldFound = null,
        });
        var columnNames = new List<string>();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        if (!await parser.ReadAsync()) return DataFrame.Empty;
        if (hasHeader)
        {
            parser.ReadHeader();
            columnNames.AddRange(parser.HeaderRecord ?? Array.Empty<string>());
        }
        else
        {
            for (var i = 0; i < parser.Parser.Count; i++) columnNames.Add("col_" + (i + 1));
            rows.Add(BuildRow(parser, columnNames));
        }

        while (await parser.ReadAsync())
        {
            rows.Add(BuildRow(parser, columnNames));
        }
        var columns = columnNames.Select(n => new DataColumn(n, DataColumnType.Text)).ToList();
        return new DataFrame(columns, rows);
    }

    private static IReadOnlyDictionary<string, object?> BuildRow(CsvReader parser, IReadOnlyList<string> columnNames)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < columnNames.Count; i++)
        {
            row[columnNames[i]] = parser.GetField(i);
        }
        return row;
    }
}
