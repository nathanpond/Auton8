namespace AutoNate.Web.Services.Datasets;

// Schema-naming convention for the per-dataset cache tables in the
// `autonate_datastores` cluster DB. One schema per dataset (`cache_<id>`)
// owning a single `rows` table whose column shape mirrors the dataset's
// persisted ColumnSchemaJson.
public static class CachedDatasetSchemas
{
    public const string CacheTableName = "rows";

    public static string CacheSchemaFor(Guid datasetId)
        => "cache_" + datasetId.ToString("N");
}
