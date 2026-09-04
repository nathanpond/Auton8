using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Auth;

/// <summary>
/// Singleton snapshot of the authenticated-only user directory (#9).
/// </summary>
/// <remarks>
/// <c>GET /api/users/directory</c> read every row of <c>local_users</c> on every
/// call, and its own comment says it fires on every editor mount. With 16 SPA
/// call sites and no stale-time override, the cost was O(users) rows per mount,
/// per assignee picker, per comment render.
///
/// Modelled on <see cref="Menus.PageRegistrySnapshotCache"/>, and for the same
/// reason: the projected rows are **actor-invariant**. The directory blanks the
/// admin-only fields for everyone, so one database read can serve every
/// concurrent caller until a user is written or the safety TTL expires.
///
/// <b>The blanking happens here, once, on the way in.</b> That is deliberate: if
/// the cache stored full rows and each caller blanked them, a future caller that
/// forgot would serve every user's email address to any authenticated account,
/// and nothing about the call site would look wrong. Storing only what may be
/// returned makes the leak unrepresentable rather than merely unlikely.
/// </remarks>
public sealed class UserDirectorySnapshotCache(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<UserDirectorySnapshotCache> logger)
{
    /// <summary>
    /// Safety net for a write that bypassed <see cref="Invalidate"/> — an
    /// out-of-band SQL fix-up, or another replica's write in a multi-replica
    /// deployment. Bounds worst-case staleness without making user mutations
    /// visibly slower.
    /// </summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DateTime _expiresAtUtc = DateTime.MinValue;
    private IReadOnlyList<LocalUser> _snapshot = [];

    public async Task<IReadOnlyList<LocalUser>> GetAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _expiresAtUtc) return _snapshot;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-checked under the lock: a concurrent caller may have refreshed
            // while this one waited, so a stampede costs one query, not many.
            if (DateTime.UtcNow < _expiresAtUtc) return _snapshot;

            await using var db = await dbContextFactory
                .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Projected in SQL to the columns the directory may expose. The
            // admin-only ones are never read, so they cannot be served by
            // accident even if this snapshot is passed somewhere careless.
            var rows = await db.LocalUsers.AsNoTracking()
                .OrderBy(u => u.Username)
                .Select(u => new LocalUser
                {
                    Id = u.Id,
                    UserId = u.UserId,
                    Username = u.Username,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    CreatedDate = u.CreatedDate,
                    // Blanked by construction — see the class remarks.
                    Email = string.Empty,
                    IdpKey = string.Empty,
                    LastLoginDate = null,
                    FailedLoginAttempts = 0,
                    IsLocked = false,
                    LockedAtUtc = null,
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _snapshot = rows;
            _expiresAtUtc = DateTime.UtcNow + SnapshotTtl;
            logger.LogDebug("User directory snapshot refreshed: {Count} users.", rows.Count);
            return _snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Drops the snapshot so the next read repopulates it.</summary>
    /// <remarks>
    /// Called from the user store on every write. A user created through the
    /// admin screen has to appear in an assignee picker immediately — waiting
    /// out a 30-second TTL would read as the create having failed.
    /// </remarks>
    public void Invalidate() => _expiresAtUtc = DateTime.MinValue;
}
