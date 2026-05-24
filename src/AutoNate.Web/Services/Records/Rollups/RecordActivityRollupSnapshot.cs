namespace AutoNate.Web.Services.Records.Rollups;

// Source-side payload for the rollup projection: one (record_type, day)
// bucket with counts. The feed produces these by aggregating the records
// table; the projection writes them through to record_activity_rollup_cache.
public sealed record class RecordActivityRollupSnapshot(
    Guid RecordTypeId,
    DateOnly BucketDay,
    int RecordsCreated,
    int RecordsUpdated,
    int RecordsArchived);
