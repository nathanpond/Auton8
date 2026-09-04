using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Identity;

/// <summary>What a reconciliation did, so the caller can audit it.</summary>
/// <remarks>
/// Added and removed are separate lists rather than a "changed" flag because
/// each one is an access change that belongs on the record individually — a
/// grant and a revocation are not the same event to anyone reading the audit
/// trail later.
/// </remarks>
public sealed record ClaimGroupReconciliationResult(
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Removed)
{
    public static readonly ClaimGroupReconciliationResult NoChange = new([], []);

    public bool ChangedAnything => Added.Count > 0 || Removed.Count > 0;
}

public interface IClaimGroupReconciler
{
    /// <summary>
    /// Brings a user's IdP-derived group memberships into line with their claims.
    /// </summary>
    Task<ClaimGroupReconciliationResult> ReconcileAsync(
        Guid userId,
        Guid providerId,
        IReadOnlyDictionary<string, string[]> claims,
        CancellationToken ct);

    /// <summary>
    /// Answers "what would these claims grant?" without writing anything.
    /// </summary>
    Task<IReadOnlyList<Guid>> PreviewAsync(
        Guid providerId, IReadOnlyDictionary<string, string[]> claims, CancellationToken ct);
}

/// <summary>
/// Turns identity-provider claims into Auton8 group membership, on every
/// federated sign-in.
/// </summary>
/// <remarks>
/// #92 exists because after #90 a federated user signs in and has nothing —
/// every one of them needs an administrator to grant access by hand, which is
/// the burden federation was supposed to remove.
///
/// Three properties do the work, and each is load-bearing:
///
/// <list type="number">
/// <item>
/// It runs on <em>every</em> sign-in, not only the first. A reconciler that
/// only ever adds is the natural first implementation and it silently never
/// revokes, so removing someone from a group at the IdP would not remove their
/// access here — the half of federation that matters most on the day somebody
/// leaves.
/// </item>
/// <item>
/// It only ever touches rows it owns: <c>source = 'idp'</c> carrying this
/// provider's id. An administrator's manual grant is never removed by a claim
/// disappearing, and one provider cannot revoke another's grants.
/// </item>
/// <item>
/// It grants groups and never roles. Groups already hold role assignments, so
/// the group → role path stays the single place authorization is reasoned
/// about — and federation does not become a second bulk-grant path.
/// </item>
/// </list>
///
/// The whole reconciliation is one transaction. A failure part-way through
/// leaving a user with the removals applied but not the additions would be a
/// silent, partial loss of access that nothing would report.
/// </remarks>
public sealed class ClaimGroupReconciler : IClaimGroupReconciler
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClaimGroupReconciler> _log;

    public ClaimGroupReconciler(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        TimeProvider clock,
        ILogger<ClaimGroupReconciler> log)
    {
        _dbFactory = dbFactory;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Which groups a claim set grants, given a provider's mappings.
    /// </summary>
    /// <remarks>
    /// Pure, static, and the <em>only</em> place this question is answered. The
    /// preview endpoint and the sign-in path both call it, so the admin screen's
    /// "what would this grant?" cannot drift into being decorative — there is
    /// nothing for it to drift from.
    ///
    /// A claim value with no mapping grants nothing and is not an error. That is
    /// the ordinary case: an IdP hands over every group a person belongs to, and
    /// most of them mean nothing here.
    /// </remarks>
    public static IReadOnlyList<Guid> ComputeDesiredGroups(
        IEnumerable<IdentityProviderGroupMappingModel> mappings,
        IReadOnlyDictionary<string, string[]> claims)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(claims);

        var granted = new HashSet<Guid>();
        foreach (var mapping in mappings)
        {
            if (!claims.TryGetValue(mapping.ClaimType, out var values)) continue;

            // Exact match, deliberately. Ordinal rather than a culture-aware
            // comparison: a claim value is an identifier from another system,
            // and Turkish-I casing rules have no business deciding who gets in.
            if (values.Any(v => string.Equals(v, mapping.ClaimValue, StringComparison.Ordinal)))
            {
                granted.Add(mapping.GroupId);
            }
        }

        return [.. granted];
    }

    public async Task<IReadOnlyList<Guid>> PreviewAsync(
        Guid providerId, IReadOnlyDictionary<string, string[]> claims, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var mappings = await db.IdentityProviderGroupMappings.AsNoTracking()
            .Where(m => m.ProviderId == providerId)
            .ToListAsync(ct);

        return ComputeDesiredGroups(mappings, claims);
    }

    public async Task<ClaimGroupReconciliationResult> ReconcileAsync(
        Guid userId,
        Guid providerId,
        IReadOnlyDictionary<string, string[]> claims,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(claims);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var mappings = await db.IdentityProviderGroupMappings.AsNoTracking()
            .Where(m => m.ProviderId == providerId)
            .ToListAsync(ct);

        var desired = ComputeDesiredGroups(mappings, claims).ToHashSet();

        // A mapping can outlive nothing here — the schema cascades — but an
        // archived group should not be granted afresh. Existing memberships of
        // one are left alone; that is an administrator's decision to unwind.
        var archived = await db.Groups.AsNoTracking()
            .Where(g => g.IsArchived)
            .Select(g => g.Id)
            .ToListAsync(ct);
        desired.ExceptWith(archived);

        var memberships = await db.GroupMembers
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

        // Everything the user is in, however it got there. Used for additions,
        // so a group already held manually is not duplicated or downgraded.
        var held = memberships.Select(m => m.GroupId).ToHashSet();

        // Only this provider's rows are candidates for removal.
        var ours = memberships
            .Where(m => m.Source == GroupMembershipSources.Idp && m.SourceProviderId == providerId)
            .ToList();

        var now = _clock.GetUtcNow().UtcDateTime;
        var added = new List<Guid>();
        var removed = new List<Guid>();

        foreach (var groupId in desired.Where(g => !held.Contains(g)))
        {
            db.GroupMembers.Add(new Persistence.Scaffolded.GroupMember
            {
                GroupId = groupId,
                UserId = userId,
                AddedAtUtc = now,
                // The provider, not a person. AddedBy is a user id everywhere
                // else, so recording some administrator here would name someone
                // who did not do it.
                AddedBy = providerId,
                Source = GroupMembershipSources.Idp,
                SourceProviderId = providerId,
            });
            added.Add(groupId);
        }

        foreach (var membership in ours.Where(m => !desired.Contains(m.GroupId)))
        {
            db.GroupMembers.Remove(membership);
            removed.Add(membership.GroupId);
        }

        if (added.Count == 0 && removed.Count == 0)
        {
            // Nothing to commit, and nothing to audit. The steady state is a
            // user signing in with the same claims they had yesterday, so an
            // event here would be almost all of the events.
            await transaction.RollbackAsync(ct);
            return ClaimGroupReconciliationResult.NoChange;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _log.LogInformation(
            "Reconciled claims for user {UserId} through provider {ProviderId}: "
            + "{Added} group(s) granted, {Removed} revoked.",
            userId, providerId, added.Count, removed.Count);

        return new ClaimGroupReconciliationResult(added, removed);
    }
}
