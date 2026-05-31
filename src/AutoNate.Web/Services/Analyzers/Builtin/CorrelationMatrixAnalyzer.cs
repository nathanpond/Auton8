using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Pearson correlation across every numeric column pair. Output is the
// triangular (i<=j) shape so a 10-column input produces 55 rows. No config.
public sealed class CorrelationMatrixAnalyzer : IAnalyzer
{
    public string Key => "correlation-matrix";
    public string DisplayName => "Pearson correlation matrix";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var numericColumns = input.Columns
            .Where(c => c.Type == DataColumnType.Integer || c.Type == DataColumnType.Number)
            .Select(c => c.Name)
            .ToList();
        var seriesByColumn = numericColumns.ToDictionary(
            c => c,
            c => input.Rows
                .Select(r => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, c), out var d) ? d : (double?)null)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        var columns = new[]
        {
            new DataColumn("a", DataColumnType.Text),
            new DataColumn("b", DataColumnType.Text),
            new DataColumn("correlation", DataColumnType.Number),
            new DataColumn("pairs", DataColumnType.Integer),
        };
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        for (var i = 0; i < numericColumns.Count; i++)
        {
            for (var j = i; j < numericColumns.Count; j++)
            {
                var (r, pairs) = PearsonCorrelation(seriesByColumn[numericColumns[i]], seriesByColumn[numericColumns[j]]);
                rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["a"] = numericColumns[i],
                    ["b"] = numericColumns[j],
                    ["correlation"] = r,
                    ["pairs"] = (long)pairs,
                });
            }
        }
        return Task.FromResult(new DataFrame(columns, rows));
    }

    private static (double R, int Pairs) PearsonCorrelation(IReadOnlyList<double?> xs, IReadOnlyList<double?> ys)
    {
        var n = 0;
        double sumX = 0, sumY = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            if (xs[i] is null || ys[i] is null) continue;
            sumX += xs[i]!.Value;
            sumY += ys[i]!.Value;
            n++;
        }
        if (n < 2) return (double.NaN, n);
        var meanX = sumX / n;
        var meanY = sumY / n;
        double covar = 0, varX = 0, varY = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            if (xs[i] is null || ys[i] is null) continue;
            var dx = xs[i]!.Value - meanX;
            var dy = ys[i]!.Value - meanY;
            covar += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }
        var denominator = Math.Sqrt(varX * varY);
        return (denominator <= double.Epsilon ? double.NaN : covar / denominator, n);
    }
}
