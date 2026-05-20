using AutoNate.Web.Services.Records;

namespace AutoNate.Web.Tests;

// Stub resolver for Authorizer ctor sites that don't exercise the
// record-SQL filter path (or that exercise it against test DBs where
// the real RecordTypeShortCodeCache initializer hasn't run). Returns an
// empty map — the Authorizer falls back to a direct DB load when this
// is the case, which matches the pre-cache behavior.
internal sealed class EmptyRecordTypeShortCodeResolver : IRecordTypeShortCodeResolver
{
    public static readonly EmptyRecordTypeShortCodeResolver Instance = new();

    public bool TryGetShortCode(Guid recordTypeId, out string shortCode)
    {
        shortCode = string.Empty;
        return false;
    }

    public IReadOnlyDictionary<string, Guid> ShortCodeToId { get; } =
        new Dictionary<string, Guid>(StringComparer.Ordinal);
}
