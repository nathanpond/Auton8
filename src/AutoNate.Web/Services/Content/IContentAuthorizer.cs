using System.Security.Claims;
using AutoNate.Web.Authorization;

namespace AutoNate.Web.Services.Content;

// Authorization for the content hierarchy kinds (project / cabinet / notebook /
// page). Distinct from IAuthorizer because the rule is "closest-ancestor
// override wins, otherwise the project-role baseline applies" — which doesn't
// fit the generic "OR allows AND NOT OR denies" model used elsewhere.
public interface IContentAuthorizer
{
    // Single-resource gate. Resolves the resource's project, applies the
    // project_members baseline, looks for closest-ancestor overrides in
    // permission_grants, and enforces deletions_locked for Delete actions on
    // anything other than notes.
    Task<AuthDecision> AuthorizeAsync(
        ClaimsPrincipal actor,
        string kind,
        Guid resourceId,
        string action,
        CancellationToken ct);

    // Set of resource IDs of the given kind that `actor` may perform `action`
    // on. Used by list endpoints to filter their query. Unrestricted=true
    // indicates a super-admin actor (no filtering needed). The caller is
    // expected to combine this with any other filters (e.g. project_id).
    Task<ContentAccessSet> GetAllowedIdsAsync(
        ClaimsPrincipal actor,
        string kind,
        string action,
        CancellationToken ct);

    // Membership lookups for endpoints that need to gate on role itself
    // (e.g. the deletions-lock toggle and member-management endpoints are
    // Owner-only by design and don't flow through the override system).
    Task<ProjectRole?> GetProjectRoleAsync(
        ClaimsPrincipal actor,
        Guid projectId,
        CancellationToken ct);

    Task<bool> IsProjectOwnerAsync(
        ClaimsPrincipal actor,
        Guid projectId,
        CancellationToken ct);
}

public sealed record class ContentAccessSet
{
    public bool Unrestricted { get; init; }

    public IReadOnlySet<Guid> AllowedIds { get; init; } = new HashSet<Guid>();

    public static ContentAccessSet UnrestrictedAccess() => new() { Unrestricted = true };

    public static ContentAccessSet From(IEnumerable<Guid> ids) =>
        new() { AllowedIds = ids.ToHashSet() };

    public static readonly ContentAccessSet Empty = new();
}
