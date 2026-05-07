using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Auth;

public interface ILocalUserStore
{
    Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default);

    Task<LocalUserPage> ListPagedAsync(ListLocalUsersRequest request, CancellationToken cancellationToken = default);

    Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    // By-id lookup the delete endpoint uses to snapshot the username into the
    // audit event before the row is gone, so the audit log shows "alice"
    // instead of a bare numeric id.
    Task<LocalUser?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    // Lookup by the user's stable Guid identifier (`UserId`). Audit events and
    // workflow process variables carry this shape; UnlockAccountBehavior
    // resolves through here when a workflow author hands it the Guid form.
    Task<LocalUser?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<LocalUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<LoginAttemptResult> AttemptLoginAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<LocalUser?> SetLockedAsync(long id, bool isLocked, CancellationToken cancellationToken = default);

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

public sealed record ListLocalUsersRequest(
    int Page = 0,
    int PageSize = 25,
    string? Search = null,
    string? SortBy = null,
    string? SortDir = null,
    string? Status = null);

public sealed record LocalUserPage(IReadOnlyList<LocalUser> Items, int TotalCount);
