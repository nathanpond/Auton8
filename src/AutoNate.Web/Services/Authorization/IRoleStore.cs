using System.Security.Claims;
using AutoNate.Web.Models.Authorization;

namespace AutoNate.Web.Services.Authorization;

public sealed record class CreateRoleInput(string Name, string? Description);

public sealed record class UpdateRoleInput(string? Name, string? Description);

public sealed class RoleNotFoundException(Guid id) : Exception($"Role '{id}' was not found.");

public sealed class RoleValidationException(string message) : Exception(message);

// Roles are containers for assignments only. The permissions a role conveys
// live in permission_grants (principal_kind='role'); manage them through
// IPermissionGrantStore.
public interface IRoleStore
{
    // Returns every role. Used for internal lookups (e.g. resolving role names
    // for an actor's own assignments) — not gated by the authorizer.
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default);

    // Returns only the roles the actor is permitted to view, after filtering
    // through the authorizer. Behaves identically to ListAsync when
    // Authorization:Enabled is false.
    Task<IReadOnlyList<Role>> ListAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default);

    Task<Role?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Role> CreateAsync(CreateRoleInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<Role> UpdateAsync(Guid id, UpdateRoleInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
