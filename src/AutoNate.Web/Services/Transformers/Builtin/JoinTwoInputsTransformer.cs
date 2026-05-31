using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers.Builtin;

// Inner join two input frames on a single equality key. Config:
//   leftKey  = <column name in inputs[0]>
//   rightKey = <column name in inputs[1]>
//   how      = inner | left   (default inner)
// Output columns: left columns + right columns excluding rightKey
// (rightKey is dropped to avoid the dup); right column names are
// auto-prefixed with "r_" on collision.
public sealed class JoinTwoInputsTransformer : ITransformer
{
    public string Key => "join-two-inputs";
    public string DisplayName => "Join two inputs";
    public int InputArity => 2;

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count < 2)
        {
            throw new InvalidOperationException("join-two-inputs requires two input frames.");
        }
        var left = inputs[0];
        var right = inputs[1];
        var leftKey = DataFrameOps.ConfigValue(config, "leftKey");
        var rightKey = DataFrameOps.ConfigValue(config, "rightKey");
        var how = (DataFrameOps.OptionalConfig(config, "how") ?? "inner").ToLowerInvariant();

        var leftNames = new HashSet<string>(left.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var rightProjected = right.Columns
            .Where(c => !string.Equals(c.Name, rightKey, StringComparison.OrdinalIgnoreCase))
            .Select(c => leftNames.Contains(c.Name)
                ? new DataColumn("r_" + c.Name, c.Type)
                : c)
            .ToList();
        var outColumns = left.Columns.Concat(rightProjected).ToList();

        // Build right-side hash by key.
        var rightByKey = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
        foreach (var rRow in right.Rows)
        {
            var keyValue = DataFrameOps.AsString(DataFrameOps.RowValue(rRow, rightKey));
            if (!rightByKey.TryGetValue(keyValue, out var list))
            {
                list = new List<IReadOnlyDictionary<string, object?>>();
                rightByKey[keyValue] = list;
            }
            list.Add(rRow);
        }

        var outRows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var lRow in left.Rows)
        {
            var keyValue = DataFrameOps.AsString(DataFrameOps.RowValue(lRow, leftKey));
            if (rightByKey.TryGetValue(keyValue, out var matches))
            {
                foreach (var rRow in matches)
                {
                    outRows.Add(MergeRow(lRow, rRow, rightKey, leftNames));
                }
            }
            else if (how == "left")
            {
                outRows.Add(MergeRow(lRow, EmptyRow(), rightKey, leftNames));
            }
        }
        return Task.FromResult(new DataFrame(outColumns, outRows));
    }

    private static IReadOnlyDictionary<string, object?> EmptyRow() =>
        new Dictionary<string, object?>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> MergeRow(
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right,
        string rightKey,
        HashSet<string> leftNames)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in left) row[kv.Key] = kv.Value;
        foreach (var kv in right)
        {
            if (string.Equals(kv.Key, rightKey, StringComparison.OrdinalIgnoreCase)) continue;
            var name = leftNames.Contains(kv.Key) ? "r_" + kv.Key : kv.Key;
            row[name] = kv.Value;
        }
        return row;
    }
}
