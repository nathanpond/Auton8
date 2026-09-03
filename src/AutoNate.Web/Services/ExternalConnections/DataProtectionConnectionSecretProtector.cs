using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace AutoNate.Web.Services.ExternalConnections;

public sealed class DataProtectionConnectionSecretProtector : IConnectionSecretProtector
{
    // Purpose-string is part of the DataProtection key derivation: rotating
    // the version suffix is how you'd force an upgrade if the fingerprint
    // format ever changed in a backwards-incompatible way.
    // internal, not private: DoNotRenameGuardTests (#65) asserts this value,
    // and a value a guard must read is part of the type's contract. Renaming it
    // makes every stored provider secret permanently undecryptable — see the
    // do-not-rename list in CLAUDE.md.
    internal const string Purpose = "AutoNate.ExternalConnections.v1";

    private readonly IDataProtector _protector;

    public DataProtectionConnectionSecretProtector(IDataProtectionProvider provider)
    {
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

    // Format: `xxxx…yyyy (sha256:abcdef12)`. Short enough for a UI badge; the
    // sha256 prefix lets admins distinguish two keys whose visible chars
    // happen to collide (an unlikely but real concern with rotated keys).
    public string Fingerprint(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var first = plaintext.Length >= 4 ? plaintext[..4] : plaintext;
        var last = plaintext.Length >= 8 ? plaintext[^4..] : string.Empty;
        var hashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))
            .ToLowerInvariant()[..8];

        return string.IsNullOrEmpty(last)
            ? $"{first}… (sha256:{hashHex})"
            : $"{first}…{last} (sha256:{hashHex})";
    }
}
