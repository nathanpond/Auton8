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
}
