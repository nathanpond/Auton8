using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Projections.Feeds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Records.Rollups;

// Recomputes the rollup for the last N days on every tick. The records
// table is the source of truth, and the recompute SQL is bounded by
// RecentDayWindow so each tick is cheap even for installs with millions of
// records.
//
// Older days aren't touched here — they only change if a record gets
// backdated (rare) or someone hits the admin "rebuild" button. The full
// historical recompute lives on BackfillRunner via the matching
// IProjectionBackfillSource.
public sealed class RecordActivityRollupFeed : PeriodicPollingFeed<RecordActivityRollupSnapshot>
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly RecordActivityRollupOptions _options;

    public RecordActivityRollupFeed(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<RecordActivityRollupOptions> options,
        ILogger<RecordActivityRollupFeed> logger)
        : base("records.rollup.poll", options.Value.PollInterval, logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var windowStart = DateTime.UtcNow.Date.AddDays(-Math.Max(1, _options.RecentDayWindow));

        // Three separate aggregations because the source columns differ:
        //   - created: row exists with created_at_utc in the window
        //   - updated: row exists with updated_at_utc in the window AND
        //              updated_at_utc != created_at_utc (i.e. actually mutated)
        //   - archived: same as updated, but is_archived = true
        // The projection upserts each bucket independently, so we emit all
        // three then let the projection merge them onto the row.
        var snapshots = await db.Database
            .SqlQueryRaw<RollupRow>(
                """
                WITH
                  created AS (
                    SELECT record_type_id,
                           date_trunc('day', created_at_utc)::date AS bucket,
                           COUNT(*) AS n
                    FROM records
                    WHERE created_at_utc >= {0}
                    GROUP BY 1, 2
                  ),
                  updated AS (
                    SELECT record_type_id,
                           date_trunc('day', updated_at_utc)::date AS bucket,
                           COUNT(*) AS n
                    FROM records
                    WHERE updated_at_utc >= {0}
                      AND updated_at_utc <> created_at_utc
                    GROUP BY 1, 2
                  ),
                  archived AS (
                    SELECT record_type_id,
                           date_trunc('day', updated_at_utc)::date AS bucket,
                           COUNT(*) AS n
                    FROM records
                    WHERE updated_at_utc >= {0}
                      AND is_archived = TRUE
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
                """,
                windowStart)
            .ToListAsync(cancellationToken);

        foreach (var row in snapshots)
        {
            await EmitAsync(
                new ChangeEvent<RecordActivityRollupSnapshot>(
                    ChangeOp.Upsert,
                    RecordActivityRollupProjection.BuildSourceId(row.RecordTypeId, row.BucketDay),
                    new RecordActivityRollupSnapshot(
                        row.RecordTypeId,
                        row.BucketDay,
                        row.RecordsCreated,
                        row.RecordsUpdated,
                        row.RecordsArchived),
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private sealed record RollupRow(
        Guid RecordTypeId,
        DateOnly BucketDay,
        int RecordsCreated,
        int RecordsUpdated,
        int RecordsArchived);
}
