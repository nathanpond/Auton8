namespace AutoNate.Web.Services.Datasets.Cached;

// What the DatasetRefreshProjection invokes per dataset on each tick (cron
// or manual). Refresh = (a) ensure schema/table exists, (b) truncate, (c)
// re-populate from the source. v1 does a full re-population; incremental
// refresh per connector cursor lands in Phase 2.1.
public interface ICachedDatasetMaterializer
{
    Task RefreshAsync(Guid datasetId, CancellationToken cancellationToken = default);
}
