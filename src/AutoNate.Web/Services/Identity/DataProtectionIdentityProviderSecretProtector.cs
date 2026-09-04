using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace AutoNate.Web.Services.Identity;

/// <summary>
/// Encrypts identity-provider secrets at rest.
/// </summary>
/// <remarks>
/// Modelled on <c>DataProtectionConnectionSecretProtector</c>, deliberately
/// with its own purpose string rather than sharing that one.
/// </remarks>
public interface IIdentityProviderSecretProtector
{
    byte[] Protect(string plaintext);

    string Reveal(byte[] ciphertext);

    /// <summary>A redacted value safe to show in admin UI and audit events.</summary>
    string Fingerprint(string plaintext);
}

public sealed class DataProtectionIdentityProviderSecretProtector : IIdentityProviderSecretProtector
{
    // A DataProtection purpose string is part of key derivation, so it is not
    // a label — it is half of the key. Two consequences follow, and both are
    // why this is a separate constant rather than a reuse of the
    // external-connections one:
    //
    //   * Sharing a purpose across unrelated secret classes means a rotation
    //     forced by one class forces re-entry of the other's secrets too.
    //   * Renaming this value makes every stored identity-provider secret
    //     permanently undecryptable. An organisation's SSO stops working and
    //     there is no recovery short of re-entering the secret at every
    //     provider.
    //
    // internal, not private: DoNotRenameGuardTests (#65) asserts this value,
    // and a value a guard must read is part of the type's contract. It is on
    // the do-not-rename list in CLAUDE.md for the reason above.
    internal const string Purpose = "AutoNate.IdentityProviders.v1";

    private readonly IDataProtector _protector;

    public DataProtectionIdentityProviderSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    public string Reveal(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
    }

    // Same format as the external-connections fingerprint: `xxxx…yyyy
    // (sha256:abcdef12)`. Short enough for a UI badge, and the hash prefix
    // distinguishes two secrets whose visible characters happen to collide.
    public string Fingerprint(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))[..8];

        if (plaintext.Length <= 8)
        {
            return $"…(sha256:{hash})";
        }

        return $"{plaintext[..4]}…{plaintext[^4..]} (sha256:{hash})";
    }
}
