using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace AutoNate.Web.Services.Events;

// Helpers used by Phase 4 view-event publishing. Filter hashes summarize a
// search request without leaking the row IDs that came back; the coalescer
// suppresses bus traffic on hot polling endpoints (notifications/unread-count
// is the canonical example) so the audit firehose stays bounded.
public static class ViewEventFilterHash
{
    private const int MaxFilterPayloadBytes = 4 * 1024;

    // Returns a stable SHA-256 hex digest of the JSON-serialized filter, plus
    // a "filterPreview" string capped at 4 KB with a truncation marker. The
    // hash lets auditors tell repeat queries apart; the preview lets them
    // reconstruct what was searched for without forcing them to replay the
    // request. The full filter object is never carried verbatim — the cap
    // keeps a single bloated request from dominating the stream.
    public static (string Hash, string Preview) Compute(object? filter)
    {
        if (filter is null) return ("empty", string.Empty);

        var json = JsonSerializer.Serialize(filter);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = SHA256.HashData(bytes);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var preview = bytes.Length <= MaxFilterPayloadBytes
            ? json
            : json.Substring(0, Math.Min(json.Length, MaxFilterPayloadBytes - 8)) + "…[trunc]";

        return (hash, preview);
    }
}

// Per-user coalescer for hot read endpoints. Keyed by (userId, eventType);
// returns true exactly once per sliding TTL window. Audit-grade — never
// drops every Nth request (sampling fails compliance review). Only suppresses
// repeated identical polls from the same actor inside the window.
public sealed class ViewEventCoalescer
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    public ViewEventCoalescer(IMemoryCache cache) { _cache = cache; }

    public bool ShouldPublish(Guid actorId, string eventType, TimeSpan? window = null)
    {
        var key = $"viewcoalesce:{actorId}:{eventType}";
        if (_cache.TryGetValue(key, out _))
        {
            return false;
        }
        _cache.Set(key, 1, new MemoryCacheEntryOptions
        {
            SlidingExpiration = window ?? DefaultWindow
        });
        return true;
    }
}
