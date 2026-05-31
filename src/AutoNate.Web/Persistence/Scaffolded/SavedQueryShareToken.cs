namespace AutoNate.Web.Persistence.Scaffolded;

// Anonymous-share token for a saved AQL query (Phase 3 of the Data Stores
// plan). Tokens are random opaque values; only their SHA-256 hash is
// persisted so a DB read can't reconstruct a working URL. The token's
// `IssuedBy` is the identity the anonymous request runs as — viewers
// without their own account get the issuer's source/dataset visibility.
// Revocation = setting RevokedAtUtc. Expiry = ExpiresAtUtc (null = never).
public partial class SavedQueryShareToken
{
    public Guid Id { get; set; }

    public Guid SavedQueryId { get; set; }

    public string TokenHash { get; set; } = null!;

    public Guid IssuedBy { get; set; }

    public DateTime IssuedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    // Cap how many times the token can be used. NULL = unlimited.
    public int? MaxUses { get; set; }

    public int UseCount { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    // Optional human-readable label so the issuer can remember which
    // recipient/channel the link went to ("Q3 sales for marketing").
    public string? Label { get; set; }
}
