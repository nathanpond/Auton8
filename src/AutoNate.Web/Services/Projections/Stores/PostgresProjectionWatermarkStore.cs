using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Projections.Stores;

public sealed class PostgresProjectionWatermarkStore : IProjectionWatermarkStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IProjectionHealthService _health;

    public PostgresProjectionWatermarkStore(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IProjectionHealthService health)
    {
        _dbFactory = dbFactory;
        _health = health;
    }

    public async Task<DateTimeOffset?> GetAsync(string feedName, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // ToArrayAsync rather than FirstOrDefaultAsync: EF's row-limiting-without-OrderBy
        // analyzer (warning 10103) can't see the LIMIT 1 inside the raw SQL string and would
        // otherwise log a false positive on every poll cycle. SQL guarantees at most one row.
        var rows = await db.Database
            .SqlQuery<WatermarkRow>($"""
                SELECT watermark_utc AS "WatermarkUtc"
                FROM projection_watermarks
                WHERE feed_name = {feedName}
                LIMIT 1
                """)
            .ToArrayAsync(cancellationToken);
        var row = rows.Length == 0 ? null : (WatermarkRow?)rows[0];
        return row is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(row.WatermarkUtc, DateTimeKind.Utc));
    }

    public async Task SetAsync(string feedName, DateTimeOffset watermark, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projection_watermarks (feed_name, watermark_utc, updated_at_utc)
            VALUES ({feedName}, {watermark.UtcDateTime}, NOW())
            ON CONFLICT (feed_name) DO UPDATE
              SET watermark_utc = EXCLUDED.watermark_utc,
                  updated_at_utc = NOW()
            """, cancellationToken);
        _health.RecordWatermark(feedName, watermark);
    }

    private sealed record WatermarkRow(DateTime WatermarkUtc);
}
