using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Records.Rollups;

// Full historical recompute of the record-activity rollup (archived-112).
//
// RecordActivityRollupFeed recomputes only the last RecentDayWindow days on
// each tick, and its comment already promised that "the full historical
// recompute lives on BackfillRunner via the matching
// IProjectionBackfillSource" — which did not exist, so the Rebuild button
// returned 400 and old buckets could never be repaired.
//
// Same three aggregations as the feed, with the day-window predicate removed:
// created rows, rows genuinely mutated after creation, and archived rows,
// full-outer-joined so a bucket appears if any of the three has a count.
public sealed class RecordActivityRollupBackfillSource(
    IDbContextFactory<AutoNateDbContext> dbFactory)
    : IProjectionBackfillSource<RecordActivityRollupSnapshot>
{
    public string ProjectionName => "records.record_activity_rollup_cache";

    public async IAsyncEnumerable<ChangeEvent<RecordActivityRollupSnapshot>> EnumerateAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Ordered by bucket so a long rebuild that is interrupted has written
        // a contiguous prefix of history rather than a random scatter, which
        // is what the framework's "yield in a stable order" contract is for.
        var rows = await db.Database
            .SqlQueryRaw<RollupRow>(
                """
                WITH
                  created AS (
                    SELECT record_type_id,
                           date_trunc('day', created_at_utc)::date AS bucket,
                           COUNT(*) AS n
                    FROM records
                    GROUP BY 1, 2
                  ),
                  updated AS (
                    SELECT record_type_id,
                           date_trunc('day', updated_at_utc)::date AS bucket,
                           COUNT(*) AS n
                    FROM records
                    WHERE updated_at_utc <> created_at_utc
                    GROUP BY 1, 2
                  ),
                  archived AS (
                    SELECT record_type_id,
                           date_trunc('day', updated_at_utc)::date AS bucket,
                           COUNT(*) AS n
                    FROM records
                    WHERE is_archived = TRUE
                    GROUP BY 1, 2
                  )
                SELECT
                  COALESCE(c.record_type_id, u.record_type_id, a.record_type_id) AS "RecordTypeId",
                  COALESCE(c.bucket,        u.bucket,        a.bucket)           AS "BucketDay",
                  COALESCE(c.n, 0)::int AS "RecordsCreated",
                  COALESCE(u.n, 0)::int AS "RecordsUpdated",
                  COALESCE(a.n, 0)::int AS "RecordsArchived"
                FROM created c
                FULL OUTER JOIN updated u USING (record_type_id, bucket)
                FULL OUTER JOIN archived a USING (record_type_id, bucket)
                ORDER BY 2, 1
                """)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            yield return new ChangeEvent<RecordActivityRollupSnapshot>(
                ChangeOp.Upsert,
                RecordActivityRollupProjection.BuildSourceId(row.RecordTypeId, row.BucketDay),
                new RecordActivityRollupSnapshot(
                    row.RecordTypeId,
                    row.BucketDay,
                    row.RecordsCreated,
                    row.RecordsUpdated,
                    row.RecordsArchived),
                DateTimeOffset.UtcNow);
        }
    }

    private sealed record RollupRow(
        Guid RecordTypeId,
        DateOnly BucketDay,
        int RecordsCreated,
        int RecordsUpdated,
        int RecordsArchived);
}
