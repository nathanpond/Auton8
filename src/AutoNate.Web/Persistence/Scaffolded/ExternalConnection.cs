namespace AutoNate.Web.Persistence.Scaffolded;

// Generic kind-discriminated row backing the External Connections admin page.
// Today's kinds are LlmProvider:Anthropic and LlmProvider:OpenAI; future kinds
// (SMTP, S3, identity provider) reuse the same shape so adding an integration
// doesn't mean another table. SecretCiphertext stores the api key (or
// equivalent) protected by Microsoft.AspNetCore.DataProtection — never the
// plaintext. SecretFingerprint is a redacted display value (first/last 4 chars
// + sha256 prefix) safe to show in admin UI and audit events.
public partial class ExternalConnection
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsDefault { get; set; }

    // Kind-specific configuration: base url, default model, custom headers,
    // anything the resolver needs to construct an IChatProvider (or future
    // SMTP/S3/IdP client). Schema validation is the responsibility of the
    // kind-specific metadata DTO — this column is intentionally permissive.
    public string MetadataJson { get; set; } = "{}";

    public byte[]? SecretCiphertext { get; set; }

    public string? SecretFingerprint { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
