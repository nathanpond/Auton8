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

    // Batch variant of AuthorizeAsync that evaluates the same (kind, resourceId,
    // action) against many user ids. Loads project + ancestor chain + grants +
    // memberships in a fixed number of queries (independent of userIds.Count)
    // and decides each user in-memory. Used by share-recipient flows where
    // running the single-resource path in a loop would be O(N) round trips.
    // Returns one entry per distinct user id (callers can re-look-up by id).
    Task<IReadOnlyDictionary<Guid, AuthDecision>> AuthorizeManyAsync(
        IReadOnlyCollection<Guid> userIds,
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

    // Returns every user who has access to this project through some path
    // other than project_members: SuperAdmins, holders of a global content
    // allow grant (`*` on `/*`), and principals of any permission grant whose
    // selector targets the project itself or one of its descendants
    // (cabinets / notebooks / pages). Group- and role-scoped grants are
    // expanded to their user members.
    Task<IReadOnlyList<DerivedAccess>> GetDerivedAccessAsync(
        Guid projectId, CancellationToken ct);
}

// One row per *grant* (or per SuperAdmin role assignment) that confers
// derived access to this project. The principal is shown as-is — never
// expanded into individual users.
//
// For `Grant`: GrantId/Action are populated, Resources lists the in-scope
// targets, Revokable is true iff every target the grant row carries is
// inside this project's subtree (otherwise revoking would remove access
// to resources outside the project too).
// For `Wildcard`: GrantId is the wildcard grant's id, Action="*", Resources
// empty, Revokable=false (selector is `/*`, far broader than the project).
// For `SuperAdmin`: GrantId/Action are null (the assignment lives in
// role_assignments, not permission_grants), Resources empty, Revokable=false.
public sealed record DerivedAccess(
    Guid? GrantId,
    string PrincipalKind,
    Guid PrincipalId,
    DerivedAccessSource Source,
    string? Action,
    bool Revokable,
    IReadOnlyList<DerivedResource> Resources);

// (kind, id) of a content resource that's in-scope for the grant.
public sealed record DerivedResource(string Kind, Guid Id);

public enum DerivedAccessSource
{
    SuperAdmin,
    Wildcard,
    Grant
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
