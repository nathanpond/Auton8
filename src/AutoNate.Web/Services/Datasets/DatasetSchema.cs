using System.Text.Json;

namespace AutoNate.Web.Services.Datasets;

// JSON shape persisted in `datasets.column_schema`. Locked at Dataset
// creation; the source can drift (the underlying SQL table can gain a
// column, the REST response shape can change) but the Dataset's AQL
// contract stays stable until the operator explicitly updates the
// dataset's schema.
public sealed record class DatasetColumn(
    string Name,
    // One of: "text", "bigint", "double precision", "boolean", "timestamptz".
    // Maps to AQL QueryDataType in DatasetQueryEntity. Allowlisted at the
    // ingest boundary (see CsvIngestor.EnsureSafePostgresType).
    string PostgresType);

public static class DatasetSchemaCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<DatasetColumn> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return Array.Empty<DatasetColumn>();
        var parsed = JsonSerializer.Deserialize<List<DatasetColumn>>(json, Options);
        return parsed ?? (IReadOnlyList<DatasetColumn>)Array.Empty<DatasetColumn>();
    }

    public static string Encode(IReadOnlyList<DatasetColumn> columns)
        => JsonSerializer.Serialize(columns, Options);
}
