using AutoNate.Web.Endpoints;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.SiteSettings;

// Singleton snapshot of the one-row site_appearance_settings table that the
// SPA loads on every mount via GET /api/appearance (and that the
// SiteAppearance admin preview re-reads on every save). One DB hit feeds
// every concurrent caller until an admin PATCHes the settings (which calls
// Invalidate()) or the 30s safety TTL expires, whichever comes first.
//
// Modeled exactly on PageRegistrySnapshotCache — same sliding-TTL +
// invalidation shape. Distinct cache rather than a generic IMemoryCache
// because the snapshot is a single value per process, not keyed, and the
// only legitimate eviction path is the explicit Invalidate() call from the
// PATCH handler.
public sealed class SiteAppearanceSnapshotCache(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<SiteAppearanceSnapshotCache> logger)
{
    // 30s safety net for the (rare) case where the PATCH handler's Invalidate()
    // got bypassed — e.g. an out-of-band SQL fix-up, or a multi-replica
    // deployment where the writer hits one replica and others don't know yet.
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DateTime _expiresAtUtc = DateTime.MinValue;
    private SiteAppearanceDto _snapshot = SiteAppearanceEndpoints.DefaultDto;

    public async Task<SiteAppearanceDto> GetAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _expiresAtUtc) return _snapshot;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow < _expiresAtUtc) return _snapshot;

            await using var db = await dbContextFactory
                .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await db.SiteAppearanceSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == SiteAppearanceEndpoints.SettingsId, cancellationToken)
                .ConfigureAwait(false);

            _snapshot = entity is null
                ? SiteAppearanceEndpoints.DefaultDto
                : SiteAppearanceEndpoints.EntityToDto(entity);
            _expiresAtUtc = DateTime.UtcNow + SnapshotTtl;
            logger.LogDebug(
                "SiteAppearanceSnapshotCache refreshed, valid until {ExpiresAtUtc:O}.",
                _expiresAtUtc);
            return _snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        _expiresAtUtc = DateTime.MinValue;
    }
}
