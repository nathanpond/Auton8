using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Query;

public sealed record class IssueShareTokenInput(
    Guid SavedQueryId,
    DateTime? ExpiresAtUtc,
    int? MaxUses,
    string? Label);

// Returned ONLY at issue time. The raw token never round-trips through
// any subsequent GET — every list endpoint exposes a redacted metadata
// view (`SavedQueryShareTokenDto`). Persisting only the hash means a DB
// snapshot can't be used to forge a working URL.
public sealed record class IssuedShareToken(SavedQueryShareToken Row, string RawToken);

public sealed class SavedQueryShareTokenNotFoundException(Guid id)
    : Exception($"Saved-query share token '{id}' was not found.");

public sealed class SavedQueryShareTokenInvalidException(string reason)
    : Exception(reason);

public interface ISavedQueryShareTokenStore
{
    Task<IReadOnlyList<SavedQueryShareToken>> ListForQueryAsync(
        Guid savedQueryId, CancellationToken cancellationToken = default);

    Task<IssuedShareToken> IssueAsync(
        IssueShareTokenInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default);

    // Looks up the token row by hashing the supplied raw token. Returns
    // null for revoked/expired/exhausted tokens — the public endpoint
    // maps null to 404 so a probe can't distinguish "no such token" from
    // "token disabled". On success increments use_count + stamps
    // last_used_at_utc atomically.
    Task<SavedQueryShareToken?> RedeemAsync(
        string rawToken, DateTime nowUtc, CancellationToken cancellationToken = default);
}
