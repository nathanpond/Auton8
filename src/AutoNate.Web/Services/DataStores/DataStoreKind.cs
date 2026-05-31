namespace AutoNate.Web.Services.DataStores;

// Closed set for v1 per docs/plans/2026-05-30-data-stores-implementation.md.
// Columnar and other kinds land in later phases. Persisted as a smallint
// in the `datastores.kind` column so reordering values is a breaking change.
public enum DataStoreKind
{
    FileType = 1,
    SqlType = 2,
}
