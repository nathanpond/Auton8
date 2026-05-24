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
        var row = await db.Database
            .SqlQuery<WatermarkRow>($"""
                SELECT watermark_utc AS "WatermarkUtc"
                FROM projection_watermarks
                WHERE feed_name = {feedName}
                LIMIT 1
                """)
            .FirstOrDefaultAsync(cancellationToken);
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
