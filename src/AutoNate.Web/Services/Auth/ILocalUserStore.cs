using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Auth;

public interface ILocalUserStore
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default);

    Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<LocalUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<LocalUser> CreateAsync(
        string username,
        string firstName,
        string lastName,
        string password,
        string? email = null,
        CancellationToken cancellationToken = default);

    Task<LocalUser?> UpdateAsync(
        long id,
        string username,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default);

    Task<bool> ResetPasswordAsync(long id, string password, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
