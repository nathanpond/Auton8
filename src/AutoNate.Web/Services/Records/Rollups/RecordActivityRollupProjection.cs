using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Records.Rollups;

// Translates per-bucket rollup snapshots into record_activity_rollup_cache
// rows. Idempotent: re-emitting the same (record_type, day) bucket overwrites
// the row with the latest counts.
public sealed class RecordActivityRollupProjection : IProjection<RecordActivityRollupSnapshot>
{
    private readonly RecordActivityRollupOptions _options;

    public RecordActivityRollupProjection(IOptions<RecordActivityRollupOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "records.record_activity_rollup_cache";

    public int Version => _options.CurrentProjectionVersion;

    public Type SourceType => typeof(RecordActivityRollupSnapshot);

    public async Task ApplyAsync(
        IReadOnlyList<ChangeEvent<RecordActivityRollupSnapshot>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var change in batch)
        {
            if (change.Op == ChangeOp.Delete)
            {
                // Caller-driven deletes happen on tombstoning a record-type
                // ("retire this type, drop its rollup history"). Source key
                // is `{recordTypeId}/{day}` so we can DELETE precisely.
                var (typeId, day) = ParseSourceId(change.SourceId);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM record_activity_rollup_cache WHERE record_type_id = {typeId} AND bucket_day = {day}",
                    cancellationToken);
                continue;
            }

            var s = change.Source!;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO record_activity_rollup_cache (
                    record_type_id, bucket_day, records_created, records_updated,
                    records_archived, projection_version, last_sync_at)
                VALUES (
                    {s.RecordTypeId}, {s.BucketDay}, {s.RecordsCreated}, {s.RecordsUpdated},
                    {s.RecordsArchived}, {_options.CurrentProjectionVersion}, {now})
                ON CONFLICT (record_type_id, bucket_day) DO UPDATE SET
                    records_created   = EXCLUDED.records_created,
                    records_updated   = EXCLUDED.records_updated,
                    records_archived  = EXCLUDED.records_archived,
                    projection_version = EXCLUDED.projection_version,
                    last_sync_at      = EXCLUDED.last_sync_at
                """, cancellationToken);
        }
    }

    public static string BuildSourceId(Guid recordTypeId, DateOnly day) =>
        $"{recordTypeId}/{day:yyyy-MM-dd}";

    private static (Guid TypeId, DateOnly Day) ParseSourceId(string id)
    {
        var sep = id.IndexOf('/');
        if (sep < 0) return (Guid.Empty, DateOnly.MinValue);
        return (Guid.Parse(id[..sep]), DateOnly.Parse(id[(sep + 1)..]));
    }
}
