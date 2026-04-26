using AutoNate.Web.Models.Authorization;

namespace AutoNate.Web.Services.Authorization;

public sealed record class CreatePermissionGrantInput(
    string PrincipalKind,   // "user" | "group" | "role"
    string PrincipalId,
    string Action,
    string SelectorString,
    string Effect,
    int Priority);

public sealed class PermissionGrantNotFoundException(Guid id)
    : Exception($"Permission grant '{id}' was not found.");

public sealed class PermissionGrantValidationException(string message) : Exception(message);

public interface IPermissionGrantStore
{
    Task<IReadOnlyList<PermissionGrant>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGrant>> ListForPrincipalAsync(
        string principalKind,
        string principalId,
        CancellationToken cancellationToken = default);

    Task<PermissionGrant> CreateAsync(
        CreatePermissionGrantInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
