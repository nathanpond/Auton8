namespace AutoNate.Plugins.Abstractions;

// ABI surface for tabular data flowing through Phase 4+ Transformers and
// Analyzers (docs/plans/2026-05-30-data-stores-implementation.md). Lives in
// the abstractions package so plugin-contributed transformers / analyzers
// can produce/consume it without taking a host reference. Once a plugin
// version ships against this shape, modifying any field is a versioned
// ABI break — treat additions with extreme care.
//
// Rows are name→value maps to mirror the AQL QueryResult.Rows shape the
// rest of AutoNate uses, so transformer/analyzer inputs glue directly to
// dataset outputs without a conversion step. Column order is authoritative
// for projection / display; the row dictionary doesn't preserve it.
public sealed record class DataFrame(
    IReadOnlyList<DataColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows)
{
    public static DataFrame Empty { get; } =
        new(Array.Empty<DataColumn>(), Array.Empty<IReadOnlyDictionary<string, object?>>());

    // Convenience for transformers that hand-build a single column by name.
    public DataColumn? FindColumn(string name)
    {
        foreach (var c in Columns)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }
}

public sealed record class DataColumn(string Name, DataColumnType Type);

// Closed enum so the wire-format and the in-memory shape stay aligned with
// AQL's QueryDataType. Json carries nested object/array values that
// downstream transformers can flatten via `json-flatten`.
public enum DataColumnType
{
    Text = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3,
    Date = 4,
    Json = 5,
}
