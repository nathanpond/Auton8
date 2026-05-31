namespace AutoNate.Web.Services.DataStores.Sql;

// Config under `DataStores:Sql:*`. The connection string itself is read
// from `ConnectionStrings:Datastores` (additive to Default) so the host
// can keep a single Npgsql config block per cluster while still pointing
// to a different database name with potentially different creds.
public sealed class DatastoresDatabaseOptions
{
    public const string SectionName = "DataStores:Sql";

    // The shared role used by ingest jobs (CSV → table). Created at startup
    // if it doesn't already exist. Each per-datastore schema is owned by
    // this role; per-datastore READ-ONLY roles are added at create time and
    // granted SELECT on its schema only.
    public string WriterRole { get; set; } = "autonate_datastore_writer";

    // Generated WriterRole password lives in `WriterRolePassword`. Operators
    // should set this via secret-store / env override (`DataStores__Sql__WriterRolePassword`).
    // If unset on startup, the initializer generates a strong random value
    // and writes it to disk under DataPaths.Root/datastores-writer.secret
    // so subsequent boots reuse the same credential without rotation.
    public string? WriterRolePassword { get; set; }
}
