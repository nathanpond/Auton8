using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Services.Analyzers.Builtin;

// Lloyd's k-means with bounded K and N for safety (no `Accord.NET` dep).
// Config:
//   columns       = comma-separated numeric feature columns
//   k             = cluster count (default 3, hard cap 32)
//   maxIterations = iteration cap (default 50)
// Output preserves input rows and appends `cluster` (integer index) +
// `distanceToCentroid` (Euclidean).
public sealed class KMeansClusterAnalyzer : IAnalyzer
{
    private const int RowHardCap = 100_000;
    private const int KHardCap = 32;

    public string Key => "k-means-cluster";
    public string DisplayName => "K-means clustering";

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (input.Rows.Count > RowHardCap)
        {
            throw new InvalidOperationException(
                $"k-means-cluster input has {input.Rows.Count} rows; the v1 hard cap is {RowHardCap}. " +
                "Pre-filter or sample upstream.");
        }
        var featureCols = DataFrameOps.SplitColumnList(DataFrameOps.ConfigValue(config, "columns"));
        if (featureCols.Count == 0)
            throw new InvalidOperationException("k-means-cluster requires at least one feature column.");
        var k = Math.Min(KHardCap,
            int.TryParse(DataFrameOps.OptionalConfig(config, "k"), out var parsed) && parsed > 0 ? parsed : 3);
        var maxIters = int.TryParse(DataFrameOps.OptionalConfig(config, "maxIterations"), out var mi) && mi > 0 ? mi : 50;

        // Materialise vectors; rows with missing values get a default zero
        // vector (the alternative — dropping them — would change row alignment).
        var vectors = input.Rows
            .Select(r => featureCols
                .Select(c => DataFrameOps.TryAsDouble(DataFrameOps.RowValue(r, c), out var d) ? d : 0.0)
                .ToArray())
            .ToList();

        if (vectors.Count == 0)
        {
            return Task.FromResult(input);
        }
        if (vectors.Count < k) k = vectors.Count;

        // Deterministic seed: take the first K rows. Sufficient for the v1
        // built-in; richer initialisation (k-means++) is a follow-up.
        var centroids = vectors.Take(k).Select(v => (double[])v.Clone()).ToList();
        var assignments = new int[vectors.Count];
        var distances = new double[vectors.Count];

        for (var iter = 0; iter < maxIters; iter++)
        {
            var changed = false;
            for (var i = 0; i < vectors.Count; i++)
            {
                var (cluster, distance) = NearestCentroid(vectors[i], centroids);
                if (assignments[i] != cluster) changed = true;
                assignments[i] = cluster;
                distances[i] = distance;
            }
            if (!changed && iter > 0) break;
            // Recompute centroids.
            var sums = Enumerable.Range(0, k).Select(_ => new double[featureCols.Count]).ToList();
            var counts = new int[k];
            for (var i = 0; i < vectors.Count; i++)
            {
                var c = assignments[i];
                counts[c]++;
                for (var f = 0; f < featureCols.Count; f++) sums[c][f] += vectors[i][f];
            }
            for (var c = 0; c < k; c++)
            {
                if (counts[c] == 0) continue;
                for (var f = 0; f < featureCols.Count; f++) centroids[c][f] = sums[c][f] / counts[c];
            }
        }

        var newColumns = input.Columns.ToList();
        newColumns.Add(new DataColumn("cluster", DataColumnType.Integer));
        newColumns.Add(new DataColumn("distanceToCentroid", DataColumnType.Number));

        var rows = new List<IReadOnlyDictionary<string, object?>>(input.Rows.Count);
        for (var i = 0; i < input.Rows.Count; i++)
        {
            var copy = new Dictionary<string, object?>(input.Rows[i], StringComparer.Ordinal);
            copy["cluster"] = (long)assignments[i];
            copy["distanceToCentroid"] = distances[i];
            rows.Add(copy);
        }
        return Task.FromResult(new DataFrame(newColumns, rows));
    }

    private static (int Cluster, double Distance) NearestCentroid(double[] vector, IReadOnlyList<double[]> centroids)
    {
        var bestIdx = 0;
        var bestDist = double.MaxValue;
        for (var c = 0; c < centroids.Count; c++)
        {
            var dist = 0.0;
            for (var f = 0; f < vector.Length; f++)
            {
                var diff = vector[f] - centroids[c][f];
                dist += diff * diff;
            }
            if (dist < bestDist) { bestDist = dist; bestIdx = c; }
        }
        return (bestIdx, Math.Sqrt(bestDist));
    }
}
