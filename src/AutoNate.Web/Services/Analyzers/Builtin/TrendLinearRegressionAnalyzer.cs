using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Simple linear regression y = a + b*x. Config:
//   x = <independent numeric column>
//   y = <dependent numeric column>
// Output: single-row frame with slope/intercept/r2/n.
public sealed class TrendLinearRegressionAnalyzer : IAnalyzer
{
    public string Key => "trend-linear-regression";
    public string DisplayName => "Linear regression";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        var xCol = DataFrameOps.ConfigValue(config, "x");
        var yCol = DataFrameOps.ConfigValue(config, "y");

        var pairs = new List<(double X, double Y)>(input.Rows.Count);
        foreach (var r in input.Rows)
        {
            if (DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, xCol), out var x)
                && DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, yCol), out var y))
            {
                pairs.Add((x, y));
            }
        }
        var columns = new[]
        {
            new DataColumn("slope", DataColumnType.Number),
            new DataColumn("intercept", DataColumnType.Number),
            new DataColumn("r2", DataColumnType.Number),
            new DataColumn("n", DataColumnType.Integer),
        };
        if (pairs.Count < 2)
        {
            var rowEmpty = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["slope"] = null,
                ["intercept"] = null,
                ["r2"] = null,
                ["n"] = (long)pairs.Count,
            };
            return Task.FromResult(new DataFrame(columns, new[] { (IReadOnlyDictionary<string, object?>)rowEmpty }));
        }
        var meanX = pairs.Average(p => p.X);
        var meanY = pairs.Average(p => p.Y);
        double covar = 0, varX = 0, varY = 0;
        foreach (var (x, y) in pairs)
        {
            var dx = x - meanX;
            var dy = y - meanY;
            covar += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }
        var slope = varX <= double.Epsilon ? 0 : covar / varX;
        var intercept = meanY - slope * meanX;
        var r2 = varX <= double.Epsilon || varY <= double.Epsilon
            ? double.NaN
            : (covar * covar) / (varX * varY);
        var row = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slope"] = slope,
            ["intercept"] = intercept,
            ["r2"] = r2,
            ["n"] = (long)pairs.Count,
        };
        return Task.FromResult(new DataFrame(columns, new[] { (IReadOnlyDictionary<string, object?>)row }));
    }
}
