using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Projections.Stores;

public sealed class PostgresProjectionVersionStore : IProjectionVersionStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public PostgresProjectionVersionStore(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ProjectionVersionRecord?> GetActiveAsync(string name, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // ToArrayAsync rather than FirstOrDefaultAsync: EF's row-limiting-without-OrderBy
        // analyzer (warning 10103) can't see the LIMIT 1 inside the raw SQL string and would
        // otherwise log a false positive on every poll cycle. SQL guarantees at most one row.
        var rows = await db.Database
            .SqlQuery<VersionRow>($"""
                SELECT name AS "Name", version AS "Version", status AS "Status",
                       started_at_utc AS "StartedAtUtc", completed_at_utc AS "CompletedAtUtc"
                FROM projection_versions
                WHERE name = {name} AND status = 'active'
                LIMIT 1
                """)
            .ToArrayAsync(cancellationToken);
        return rows.Length == 0 ? null : Map(rows[0]);
    }

    public async Task SetActiveAsync(string name, int version, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projection_versions (name, version, status, started_at_utc, completed_at_utc)
            VALUES ({name}, {version}, 'active', NOW(), NOW())
            ON CONFLICT (name, version) DO UPDATE
              SET status = 'active', completed_at_utc = NOW()
            """, cancellationToken);
    }

    public async Task RecordShadowStartAsync(string name, int version, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projection_versions (name, version, status, started_at_utc, completed_at_utc)
            VALUES ({name}, {version}, 'shadow', NOW(), NULL)
            ON CONFLICT (name, version) DO UPDATE
              SET status = 'shadow', started_at_utc = NOW(), completed_at_utc = NULL
            """, cancellationToken);
    }

    public async Task PromoteShadowAsync(string name, int version, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE projection_versions
            SET status = 'retired'
            WHERE name = {name} AND status = 'active'
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE projection_versions
            SET status = 'active', completed_at_utc = NOW()
            WHERE name = {name} AND version = {version}
            """, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectionVersionRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database
            .SqlQuery<VersionRow>($"""
                SELECT name AS "Name", version AS "Version", status AS "Status",
                       started_at_utc AS "StartedAtUtc", completed_at_utc AS "CompletedAtUtc"
                FROM projection_versions
                ORDER BY name, version DESC
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    private static ProjectionVersionRecord Map(VersionRow row) =>
        new(row.Name, row.Version, ParseStatus(row.Status),
            DateTime.SpecifyKind(row.StartedAtUtc, DateTimeKind.Utc),
            row.CompletedAtUtc is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null);

    private static ProjectionVersionStatus ParseStatus(string s) => s switch
    {
        "active" => ProjectionVersionStatus.Active,
        "shadow" => ProjectionVersionStatus.Shadow,
        "retired" => ProjectionVersionStatus.Retired,
        _ => throw new InvalidOperationException($"Unknown projection version status '{s}'.")
    };

    private sealed record VersionRow(
        string Name,
        int Version,
        string Status,
        DateTime StartedAtUtc,
        DateTime? CompletedAtUtc);
}
