using System.Security.Claims;

namespace AutoNate.Web.Authorization.Evaluator;

// Per-kind handler that knows whether a (kind, id) target satisfies the
// actor's grants for a given action. DB-backed kinds typically wrap
// FilterQueryAsync against a single-row queryable; external-system kinds
// (Flowable) fetch the entity over HTTP and evaluate selectors in memory.
public interface IInstanceAuthorizer
{
    string Kind { get; }

    Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Which of <paramref name="targetIds"/> the actor may perform the action on.
    /// </summary>
    /// <remarks>
    /// Exists so a batched permission check costs one query per
    /// (kind, action) rather than one per item — <c>POST /api/auth/check</c>
    /// drives two checks per row, so a 25-row list page was 50 sequential
    /// round-trips behind a single HTTP call (#5).
    ///
    /// <b>The default implementation loops <see cref="ExistsAndAuthorizedAsync"/>,
    /// and that is deliberate.</b> Fifteen kinds implement this interface, in the
    /// code that decides who may do what; making the method required would mean
    /// editing all fifteen to gain speed on three, and every edit is a chance to
    /// get an access decision wrong. Kinds that are not hot keep byte-identical
    /// behaviour by not being touched at all.
    ///
    /// An override MUST return exactly the ids the loop would have returned.
    /// <c>InstanceAuthorizerBatchEquivalenceTests</c> asserts that for every
    /// registered kind, because a batch that is fast and wrong still returns
    /// 200 with a plausible-looking list.
    /// </remarks>
    async Task<IReadOnlySet<string>> FilterAuthorizedIdsAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        IReadOnlyCollection<string> targetIds,
        CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in targetIds)
        {
            if (await ExistsAndAuthorizedAsync(authorizer, actor, action, id, cancellationToken))
            {
                allowed.Add(id);
            }
        }
        return allowed;
    }
}
