namespace AutoNate.Web.Services.ExternalConnections;

// Encrypts the api key (or equivalent secret) for an external_connection row
// using ASP.NET Core DataProtection. The store never exposes plaintext to its
// callers — only the connection-resolver layer (and only at the moment it
// constructs an outbound client) calls Reveal. Fingerprint produces a short,
// collision-resistant display value safe to show in admin UI and audit
// payloads (first/last 4 plaintext chars + sha256 prefix).
public interface IConnectionSecretProtector
{
    byte[] Protect(string plaintext);

    string Reveal(byte[] ciphertext);

    string Fingerprint(string plaintext);
}
