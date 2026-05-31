using System.Globalization;
using System.Text;
using AutoNate.Plugins.Abstractions;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Renders all input rows as one CSV blob held in a single output row's
// `content` column. Config:
//   includeHeader = true | false  (default true)
public sealed class JsonToCsvTransformer : ITransformer
{
    public string Key => "json-to-csv";
    public string DisplayName => "Rows → CSV";

    public async Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return DataFrame.Empty;
        var input = inputs[0];
        var includeHeader = !string.Equals(DataFrameOps.OptionalConfig(config, "includeHeader"), "false",
            StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        await using (var writer = new StringWriter(sb))
        await using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
        {
            if (includeHeader)
            {
                foreach (var c in input.Columns) csv.WriteField(c.Name);
                await csv.NextRecordAsync();
            }
            foreach (var row in input.Rows)
            {
                foreach (var c in input.Columns)
                {
                    csv.WriteField(DataFrameOps.AsString(DataFrameOps.RowValue(row, c.Name)));
                }
                await csv.NextRecordAsync();
            }
        }

        var outRow = new Dictionary<string, object?>(StringComparer.Ordinal) { ["content"] = sb.ToString() };
        return new DataFrame(
            new[] { new DataColumn("content", DataColumnType.Text) },
            new[] { (IReadOnlyDictionary<string, object?>)outRow });
    }
}
