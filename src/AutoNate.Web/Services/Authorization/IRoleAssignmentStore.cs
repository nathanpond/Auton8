using AutoNate.Web.Models.Authorization;

namespace AutoNate.Web.Services.Authorization;

public sealed record class CreateRoleAssignmentInput(
    Guid RoleId,
    string PrincipalKind,   // "user" | "group"
    string PrincipalId,
    string? ScopeString);

public sealed class RoleAssignmentNotFoundException(Guid id)
    : Exception($"Role assignment '{id}' was not found.");

public sealed class RoleAssignmentValidationException(string message) : Exception(message);

public interface IRoleAssignmentStore
{
    Task<IReadOnlyList<RoleAssignment>> ListByRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleAssignment>> ListForPrincipalAsync(
        string principalKind,
        string principalId,
        CancellationToken cancellationToken = default);

    // Batch variant: returns assignments for any principal in `principalIds`
    // sharing the same kind. Single SQL query (translates to
    // `WHERE principal_kind = @kind AND principal_id = ANY(@ids)`), so
    // callers like /api/auth/me can resolve all of a user's group-derived
    // assignments in one round trip instead of N. Returns empty when
    // `principalIds` is empty (avoids issuing a useless query).
    Task<IReadOnlyList<RoleAssignment>> ListForPrincipalsAsync(
        string principalKind,
        IReadOnlyCollection<string> principalIds,
        CancellationToken cancellationToken = default);

    Task<RoleAssignment> AssignAsync(
        CreateRoleAssignmentInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    // Needed so the revoke endpoint can authorize against the role the
    // assignment actually names (archived-182). Without it the route could only be
    // gated kind-level, which cannot tell one role from another.
    Task<RoleAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}
