using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using MenuItemEntity = AutoNate.Web.Persistence.Scaffolded.MenuItem;

namespace AutoNate.Web.Services.Menus;

// Singleton snapshot of every page/route/template menu_items row that the
// SPA's catch-all router needs to enumerate. Read by EfCoreMenuStore.
// ListPagesAsync on the hot path (fires on every SPA mount) and invalidated
// from the per-request store whenever a menu_item is created/updated/deleted
// or the tree is replaced.
//
// Why a snapshot at all: ListPagesAsync's underlying query is invariant of
// the calling actor — auth filtering happens *after* the materialized rows
// land in the app. So one DB hit feeds every concurrent call until an admin
// edits a menu (or the 30s safety TTL expires, whichever comes first).
//
// Why not IMemoryCache: the snapshot is one row per process, never per-key,
// and the safety TTL is the only reason to evict — Postgres-side fan-out
// from a write to "every replica's snapshot is now stale" is the explicit
// Invalidate() call from the store. A plain SemaphoreSlim + sliding-TTL
// pattern is simpler and avoids the cost of MemoryCache's per-key bookkeeping.
public sealed class PageRegistrySnapshotCache(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<PageRegistrySnapshotCache> logger)
{
    // 30s safety net for the (rare) case where the store's Invalidate() got
    // bypassed — e.g. an out-of-band SQL fix-up to menu_items, a future
    // background job that rewrites rows, or a multi-replica deployment
    // where the writer hits one replica and other replicas' caches don't
    // know yet. Tightens the worst-case staleness window without making
    // mutations visibly slower.
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DateTime _expiresAtUtc = DateTime.MinValue;
    private IReadOnlyList<MenuItemEntity> _snapshot = Array.Empty<MenuItemEntity>();

    // Returns the cached snapshot, refreshing from the DB on miss or
    // expiration. Concurrent callers during a refresh wait on the lock so
    // a stampede on the menu_items table only ever runs as a single query.
    public async Task<IReadOnlyList<MenuItemEntity>> GetAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _expiresAtUtc) return _snapshot;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock: another thread may have
            // refreshed while we were waiting.
            if (DateTime.UtcNow < _expiresAtUtc) return _snapshot;

            await using var db = await dbContextFactory
                .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var rows = await db.MenuItems.AsNoTracking()
                .Where(i => (i.ItemType == "page" || i.ItemType == "route" || i.ItemType == "template") && i.IsVisible)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _snapshot = rows;
            _expiresAtUtc = DateTime.UtcNow + SnapshotTtl;
            logger.LogDebug(
                "PageRegistrySnapshotCache refreshed: {Count} entries, valid until {ExpiresAtUtc:O}.",
                rows.Count, _expiresAtUtc);
            return _snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // Marks the cache as expired so the next GetAsync re-queries the DB.
    // Called by EfCoreMenuStore after every menu_item write so admins see
    // their changes immediately, not on the next 30s tick.
    public void Invalidate()
    {
        _expiresAtUtc = DateTime.MinValue;
    }
}
