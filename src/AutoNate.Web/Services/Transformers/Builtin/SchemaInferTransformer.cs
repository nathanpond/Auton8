using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Pass rows through unchanged but recompute each column's declared type
// from a sample of values. Useful right after csv-to-json / xlsx-to-csv
// where the upstream transformer produces every column as Text.
public sealed class SchemaInferTransformer : ITransformer
{
    public string Key => "schema-infer";
    public string DisplayName => "Infer schema";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var newColumns = input.Columns.Select(c =>
        {
            var inferred = DataFrameOps.InferColumnType(input.Rows.Select(r => DataFrameOps.RowValue(r, c.Name)));
            return new DataColumn(c.Name, inferred);
        }).ToList();
        return Task.FromResult(new DataFrame(newColumns, input.Rows));
    }
}
