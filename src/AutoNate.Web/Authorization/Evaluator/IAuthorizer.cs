using System.Security.Claims;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;

namespace AutoNate.Web.Authorization.Evaluator;

public interface IAuthorizer
{
    Task<AuthDecision> AuthorizeAsync(
        ClaimsPrincipal actor,
        string action,
        EntityRef target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes several targets at once, one query per (kind, action) group.
    /// </summary>
    /// <remarks>
    /// Same verdicts as calling <see cref="AuthorizeAsync"/> per item — it runs
    /// the same decision code and the same filter hook, and only asks the
    /// database in groups. Added for <c>POST /api/auth/check</c>, which the SPA
    /// fires on every gated list view with two checks per row (#5).
    /// </remarks>
    /// <remarks>
    /// Defaults to looping <see cref="AuthorizeAsync"/>, so every existing
    /// implementation keeps working untouched and any that does not batch is
    /// merely slower, never wrong. <c>Authorizer</c> overrides it.
    /// </remarks>
    async Task<IReadOnlyList<AuthDecision>> AuthorizeManyAsync(
        ClaimsPrincipal actor,
        IReadOnlyList<(string Action, EntityRef Target)> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var results = new AuthDecision[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            results[i] = await AuthorizeAsync(
                actor, requests[i].Action, requests[i].Target, cancellationToken);
        }
        return results;
    }

    // Filters an IQueryable to the entities the actor is permitted to perform
    // `action` on, given the current grant graph. The DbContext is passed
    // explicitly so compiled selectors can issue cross-table subqueries
    // (e.g. JOINs against entity_edges) under the same connection.
    Task<IQueryable<T>> FilterQueryAsync<T>(
        AutoNateDbContext db,
        ClaimsPrincipal actor,
        string kind,
        string action,
        IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class;

    Task<CapabilitySummary> GetCapabilitiesAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default);

    // External-system equivalent of FilterQueryAsync: load the actor's grants,
    // apply a caller-supplied selector matcher to each, and combine via the
    // standard `OR(allows) AND NOT OR(denies)` rule. Returns true iff the
    // entity the matcher describes is authorized. Used by Flowable kinds
    // that can't be expressed as an EF query.
    Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal actor,
        string kind,
        string action,
        Func<SelectorAst, bool> selectorMatcher,
        CancellationToken cancellationToken = default);

    // Returns a SQL boolean fragment + parameters that, when AND'd onto a
    // raw SQL query against the `records` table, restricts the result set
    // to rows the actor may perform `action` on. The fragment uses
    // `{N}` placeholder syntax compatible with ExecuteSqlRawAsync, with
    // indices starting at `parameterOffset`. Result is `Open` when no
    // filter is needed (auth disabled / SuperAdmin) and `Closed` ("(FALSE)")
    // when no allow grant matches. Used by raw-SQL record list paths
    // (SearchAsync) that can't go through FilterQueryAsync's IQueryable.
    Task<RecordSqlFilter> BuildRecordSqlFilterAsync(
        ClaimsPrincipal actor,
        string action,
        int parameterOffset,
        CancellationToken cancellationToken = default);

    // Inspection helper for the admin debugger. Evaluates each grant the
    // given user has against a specific target and returns both the final
    // decision and the per-grant trace, so an admin can see exactly which
    // rule (or absence of one) drove the answer.
    Task<AuthExplanation> ExplainAsync(
        Guid asUserId,
        string action,
        EntityRef target,
        CancellationToken cancellationToken = default);
}

public sealed record class AuthExplanation
{
    public required AuthEffect Effect { get; init; }
    public required string Reason { get; init; }
    public required Guid AsUserId { get; init; }
    public required bool IsSuperAdmin { get; init; }
    public required IReadOnlyList<Guid> GroupIds { get; init; }
    public required IReadOnlyList<Guid> RoleIds { get; init; }
    public required IReadOnlyList<GrantConsideration> Grants { get; init; }
}

public sealed record class GrantConsideration
{
    public required string PrincipalKind { get; init; }
    public required string PrincipalId { get; init; }
    public string? PrincipalName { get; init; }
    public required string Action { get; init; }
    public required string SelectorString { get; init; }
    public required AuthEffect Effect { get; init; }
    // null: not evaluated (kind not supported by the debugger).
    // true / false: grant matched / didn't match the target.
    public bool? Matched { get; init; }
    public string? Error { get; init; }
}

public sealed class RecordSqlFilter
{
    public bool AccessOpen { get; init; }

    public string Sql { get; init; } = string.Empty;

    public IReadOnlyList<object?> Parameters { get; init; } = Array.Empty<object?>();

    public static readonly RecordSqlFilter Open = new() { AccessOpen = true };
    public static readonly RecordSqlFilter Closed = new() { Sql = "(FALSE)" };
}
