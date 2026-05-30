namespace AutoNate.Web.Persistence.Scaffolded;

// One row per table in a SqlType DataStore. The actual table lives in the
// `autonate_datastores` cluster DB inside the per-datastore schema; this
// row is the host's index over those, plus the column-schema JSON the SPA
// renders for browsing.
public partial class DataStoreTable
{
    public Guid Id { get; set; }

    public Guid DataStoreId { get; set; }

    // Schema in `autonate_datastores` (`ds_<datastoreid>` convention).
    public string SchemaName { get; set; } = null!;

    public string TableName { get; set; } = null!;

    // JSON-encoded array of {name, type, nullable} — produced by the CSV
    // ingestor's schema-infer pass and editable in the confirm step.
    public string ColumnSchemaJson { get; set; } = "[]";

    public long RowCount { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
