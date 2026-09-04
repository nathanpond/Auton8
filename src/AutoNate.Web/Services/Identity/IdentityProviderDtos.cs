namespace AutoNate.Web.Services.Identity;

/// <summary>
/// What a read endpoint returns for a provider.
/// </summary>
/// <remarks>
/// There is deliberately no secret property, and no plaintext anywhere on this
/// type. #87's acceptance criterion is that a secret can be set and replaced
/// but never read back, and the regression it names is "a DTO gaining the field
/// later" — so the DTO is the place to make that structurally impossible rather
/// than merely currently true. <see cref="HasSecret"/> and
/// <see cref="SecretFingerprint"/> are what the admin screen needs: whether one
/// is set, and enough to tell two apart.
/// </remarks>
public sealed record IdentityProviderDto(
    Guid Id,
    string Kind,
    string DisplayName,
    string Slug,
    bool IsEnabled,
    string? OidcAuthority,
    string? OidcClientId,
    string? OidcScopes,
    string? SamlEntityId,
    string? SamlMetadataUrl,
    bool HasSamlMetadataXml,
    string? SamlSigningCertificate,
    bool HasSecret,
    string? SecretFingerprint,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateIdentityProviderRequest(
    string? Kind,
    string? DisplayName,
    string? Slug,
    bool? IsEnabled,
    string? OidcAuthority,
    string? OidcClientId,
    string? OidcScopes,
    string? SamlEntityId,
    string? SamlMetadataUrl,
    string? SamlMetadataXml,
    string? SamlSigningCertificate,
    string? Secret);

public sealed record UpdateIdentityProviderRequest(
    string? DisplayName,
    string? OidcAuthority,
    string? OidcClientId,
    string? OidcScopes,
    string? SamlEntityId,
    string? SamlMetadataUrl,
    string? SamlMetadataXml,
    string? SamlSigningCertificate,
    /// <summary>
    /// Null leaves the stored secret alone; a value replaces it; empty clears
    /// it. Distinguishing "not supplied" from "cleared" is why this is a
    /// nullable string rather than a plain one — a PATCH that omits the field
    /// must not silently wipe the secret.
    /// </summary>
    string? Secret);

/// <summary>The result of the "test configuration" action.</summary>
public sealed record IdentityProviderTestResult(
    bool Success,
    string Summary,
    IReadOnlyList<string> Findings);
