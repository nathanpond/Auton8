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

    Task<RoleAssignment> AssignAsync(
        CreateRoleAssignmentInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}
