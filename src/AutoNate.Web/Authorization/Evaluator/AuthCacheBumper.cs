using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Authorization.Evaluator;

// Bumps the auth_cache_version row whenever grant data changes. Process-wide
// in-memory caches built around the version number become stale automatically.
public sealed class AuthCacheBumper
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public AuthCacheBumper(IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task BumpAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE auth_cache_version SET version = version + 1, bumped_at_utc = NOW() WHERE id = 1",
            cancellationToken);
    }
}
