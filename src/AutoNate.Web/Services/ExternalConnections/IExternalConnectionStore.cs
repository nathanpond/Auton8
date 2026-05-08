using System.Text.Json;

namespace AutoNate.Web.Services.ExternalConnections;

// All ListAsync/GetAsync results return ExternalConnectionRow — the
// plaintext-free DTO. The plaintext secret only escapes through
// IChatProviderResolver via IConnectionSecretProtector; the store deliberately
// does not expose it.
public interface IExternalConnectionStore
{
    Task<IReadOnlyList<ExternalConnectionRow>> ListAsync(
        string? kind,
        CancellationToken cancellationToken = default);

    Task<ExternalConnectionRow?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ExternalConnectionRow> CreateAsync(
        CreateExternalConnectionInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<ExternalConnectionRow?> UpdateAsync(
        Guid id,
        UpdateExternalConnectionInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<ExternalConnectionRow?> SetDefaultAsync(
        Guid id,
        Guid actorId,
        CancellationToken cancellationToken = default);

    // Returns the plaintext secret the resolver needs to construct an
    // outbound client. Must NEVER be logged. Callers are responsible for
    // discarding the string promptly.
    Task<RevealedConnection?> RevealForResolverAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

// Plaintext-free row. SecretFingerprint is safe to show in admin UI / audit.
public sealed record class ExternalConnectionRow(
    Guid Id,
    string Kind,
    string Name,
    string? Description,
    bool IsEnabled,
    bool IsDefault,
    JsonElement Metadata,
    string? SecretFingerprint,
    DateTime CreatedAtUtc,
    Guid CreatedBy,
    DateTime UpdatedAtUtc,
    Guid UpdatedBy);

public sealed record class CreateExternalConnectionInput(
    string Kind,
    string Name,
    string? Description,
    bool IsEnabled,
    JsonElement Metadata,
    string? Secret);

// Secret omitted = keep existing. Secret = "" = clear (e.g. disabling
// an integration without deleting its config).
public sealed record class UpdateExternalConnectionInput(
    string? Name,
    string? Description,
    bool? IsEnabled,
    JsonElement? Metadata,
    string? Secret);

public sealed record class RevealedConnection(
    Guid Id,
    string Kind,
    JsonElement Metadata,
    string Secret);
