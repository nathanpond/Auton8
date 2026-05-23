namespace AutoNate.Web.Services.Query;

public enum QueryDataType
{
    String,
    Number,
    Bool,
    Date,
    Json
}

// Column descriptor for results. Carries enough metadata for the SPA to
// format cells (right-align numbers, render dates, bool toggles, etc.).
public sealed record QueryColumnMeta(string Name, QueryDataType DataType);

// One row is a name→value map. Order of insertion matches the column order
// from QueryResult.Columns so the SPA can iterate either way.
public sealed record QueryResult(
    IReadOnlyList<QueryColumnMeta> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long TotalCount,
    bool Truncated,
    long DurationMs);
