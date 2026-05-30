namespace AutoNate.Web.Services.Datasets;

// Persisted as smallint in `datasets.mode`. Reordering values is a breaking
// change. 1 = Virtual (passthrough, executes against the source on each
// query); 2 = Cached (materialized into autonate_datastores.cache_<id>
// by DatasetRefreshProjection on cron + on demand).
public enum DatasetMode
{
    Virtual = 1,
    Cached = 2,
}
