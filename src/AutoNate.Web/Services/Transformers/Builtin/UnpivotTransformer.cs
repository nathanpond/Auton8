using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Wide → long unpivot. Config:
//   idColumns    = comma-separated columns to carry through as identity
//   valueColumns = comma-separated columns to melt into (variable, value)
public sealed class UnpivotTransformer : ITransformer
{
    public string Key => "unpivot";
    public string DisplayName => "Unpivot (wide → long)";

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Task.FromResult(DataFrame.Empty);
        var input = inputs[0];
        var idColumns = DataFrameOps.SplitColumnList(DataFrameOps.OptionalConfig(config, "idColumns"));
        var valueColumns = DataFrameOps.SplitColumnList(DataFrameOps.OptionalConfig(config, "valueColumns"));
        if (valueColumns.Count == 0)
        {
            // Default: melt everything that isn't an id column.
            valueColumns = input.Columns
                .Select(c => c.Name)
                .Where(n => !idColumns.Any(idc => string.Equals(idc, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var outColumns = new List<DataColumn>();
        foreach (var idc in idColumns)
        {
            var src = input.FindColumn(idc);
            outColumns.Add(new DataColumn(idc, src?.Type ?? DataColumnType.Text));
        }
        outColumns.Add(new DataColumn("variable", DataColumnType.Text));
        outColumns.Add(new DataColumn("value", DataColumnType.Text));

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in input.Rows)
        {
            foreach (var vc in valueColumns)
            {
                var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var idc in idColumns)
                {
                    copy[idc] = DataFrameOps.RowValue(row, idc);
                }
                copy["variable"] = vc;
                copy["value"] = DataFrameOps.RowValue(row, vc);
                rows.Add(copy);
            }
        }
        return Task.FromResult(new DataFrame(outColumns, rows));
    }
}
