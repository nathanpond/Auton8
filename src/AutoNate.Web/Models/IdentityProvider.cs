namespace AutoNate.Web.Models;

/// <summary>
/// The kinds of identity provider Auton8 can federate to.
/// </summary>
/// <remarks>
/// Stored as the lowercase string in <c>identity_providers.kind</c>. One table
/// with this discriminator rather than one table per protocol — see the schema
/// batch for why.
/// </remarks>
public static class IdentityProviderKinds
{
    public const string Oidc = "oidc";
    public const string Saml = "saml";

    public static bool IsKnown(string? kind) =>
        string.Equals(kind, Oidc, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, Saml, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A configured identity provider.
/// </summary>
/// <remarks>
/// The secret is present only as ciphertext and a redacted fingerprint. There
/// is deliberately no plaintext property anywhere on this type: #87 requires
/// that a secret set through the UI can never be read back, and the cheapest
/// way to keep that true is for the model that read paths project from to have
/// nowhere to put it.
/// </remarks>
public sealed class IdentityProviderModel
{
    public Guid Id { get; set; }

    /// <summary>One of <see cref="IdentityProviderKinds"/>.</summary>
    public string Kind { get; set; } = null!;

    /// <summary>Shown on the login page button.</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Stable identifier used in callback paths and to pick the provider on
    /// the way back from the IdP. Unique, case-insensitively.
    /// </summary>
    public string Slug { get; set; } = null!;

    public bool IsEnabled { get; set; }

    // ── OIDC ────────────────────────────────────────────────────────────────
    public string? OidcAuthority { get; set; }
    public string? OidcClientId { get; set; }
    public string? OidcScopes { get; set; }

    // ── SAML ────────────────────────────────────────────────────────────────
    public string? SamlEntityId { get; set; }
    public string? SamlMetadataUrl { get; set; }
    public string? SamlMetadataXml { get; set; }
    public string? SamlSigningCertificate { get; set; }

    // ── Secret ──────────────────────────────────────────────────────────────
    public byte[]? SecretCiphertext { get; set; }

    /// <summary>
    /// Redacted display value — safe for admin UI and audit events, and the
    /// only thing about the secret any read endpoint returns.
    /// </summary>
    public string? SecretFingerprint { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedBy { get; set; }
}
