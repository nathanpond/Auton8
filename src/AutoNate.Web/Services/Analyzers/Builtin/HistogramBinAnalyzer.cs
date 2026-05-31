using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Equal-width histogram bins on a numeric column. Config:
//   column = <numeric column>     (required)
//   bins   = <positive int>       (default 10)
public sealed class HistogramBinAnalyzer : IAnalyzer
{
    public string Key => "histogram-bin";
    public string DisplayName => "Histogram (equal-width bins)";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var column = DataFrameOps.ConfigValue(config, "column");
        var binCount = int.TryParse(DataFrameOps.OptionalConfig(config, "bins"), out var b) && b > 0 ? b : 10;

        var values = input.Rows
            .Select(r => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, column), out var d) ? d : (double?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
        var columns = new[]
        {
            new DataColumn("binIndex", DataColumnType.Integer),
            new DataColumn("binStart", DataColumnType.Number),
            new DataColumn("binEnd", DataColumnType.Number),
            new DataColumn("count", DataColumnType.Integer),
        };
        if (values.Count == 0) return Task.FromResult(new DataFrame(columns, Array.Empty<IReadOnlyDictionary<string, object?>>()));

        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < double.Epsilon) max = min + 1;
        var width = (max - min) / binCount;
        var counts = new long[binCount];
        foreach (var v in values)
        {
            var idx = (int)Math.Min(binCount - 1, Math.Floor((v - min) / width));
            counts[idx]++;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(binCount);
        for (var i = 0; i < binCount; i++)
        {
            rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["binIndex"] = (long)i,
                ["binStart"] = min + i * width,
                ["binEnd"] = min + (i + 1) * width,
                ["count"] = counts[i],
            });
        }
        return Task.FromResult(new DataFrame(columns, rows));
    }
}
