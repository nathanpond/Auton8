using Microsoft.IdentityModel.Tokens;

namespace AutoNate.Web.Services.Identity;

/// <summary>
/// Bounded, expiring record of consumed SAML assertions.
/// </summary>
/// <remarks>
/// This implements <see cref="ITokenReplayCache"/> rather than sitting alongside
/// the library, so replay detection happens inside ITfoxtec's own validation
/// (<c>Saml2Configuration.DetectReplayedTokens</c>) instead of in a parallel
/// code path that could disagree with it about which assertions were accepted.
///
/// A signed assertion is a bearer credential: the signature says it is genuine,
/// not that this is the first time it has been presented. Replay protection is
/// what says that.
///
/// It keeps its own dictionary rather than sharing the application's
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>. A shared
/// cache is a shared eviction budget, and eviction here is not a cache miss —
/// it is a consumed assertion becoming acceptable again. Whether a replay
/// succeeds should not depend on how busy some unrelated feature's caching was,
/// and a general-purpose cache is free to drop any entry at any time.
///
/// It stays bounded by expiry rather than by a cleanup job: every entry names
/// the moment it stops mattering, and each write prunes what has passed, so the
/// store holds about one validity window's worth of sign-ins. Nothing reaches it
/// unsigned — ITfoxtec validates the XML signature before the replay check runs
/// — so an anonymous caller cannot grow it at all.
///
/// **Per-instance, and that is a real limit.** Two Auton8 instances behind a
/// load balancer would each accept the same assertion once. Closing that needs a
/// shared store (Redis is already in the stack) and is a deployment decision
/// rather than part of this story — recorded on #93 and in the decision ledger
/// rather than left for someone to discover.
/// </remarks>
public sealed class SamlReplayGuard : ITokenReplayCache
{
    /// <summary>
    /// A floor on how long a consumed assertion is remembered.
    /// </summary>
    /// <remarks>
    /// An assertion whose window has already closed is still recorded briefly,
    /// so a replay cannot slip through the gap between one check and the next.
    /// </remarks>
    private static readonly TimeSpan MinimumRetention = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public SamlReplayGuard(TimeProvider clock) => _clock = clock;

    public bool TryAdd(string securityToken, DateTime expiresOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(securityToken);

        // Check-and-set under a lock. Without it, two concurrent posts of the
        // same assertion can both find it absent and both proceed — precisely
        // the race a replay guard exists to lose, and the one an attacker would
        // try, since sending the same form twice costs nothing.
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            Prune(now);

            if (_consumed.ContainsKey(securityToken)) return false;

            var floor = now + MinimumRetention;
            // The IdP's DateTime carries no offset. It is UTC by SAML's
            // definition, but its Kind may be Unspecified, and constructing a
            // DateTimeOffset from a Local-kind value would shift it.
            var stated = new DateTimeOffset(
                DateTime.SpecifyKind(expiresOn, DateTimeKind.Utc), TimeSpan.Zero);

            _consumed[securityToken] = stated > floor ? stated : floor;
            return true;
        }
    }

    public bool TryFind(string securityToken)
    {
        lock (_gate)
        {
            return _consumed.TryGetValue(securityToken, out var until)
                && until > _clock.GetUtcNow();
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (_consumed.Count == 0) return;

        // Enumerated into a list first: removing from a dictionary while
        // enumerating it is undefined.
        var stale = _consumed.Where(e => e.Value <= now).Select(e => e.Key).ToList();
        foreach (var key in stale) _consumed.Remove(key);
    }
}
