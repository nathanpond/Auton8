namespace AutoNate.Web.Services.Records;

// Synchronous lookup of a record type's short code by its id. Used by the
// signal dispatcher to evaluate per-registration record-type filters at
// dispatch time, and by IAuthorizer's record-SQL compiler to translate
// `[recordtype=lead]` predicates into the column-level id comparison.
// Implemented by RecordTypeShortCodeCache.
public interface IRecordTypeShortCodeResolver
{
    bool TryGetShortCode(Guid recordTypeId, out string shortCode);

    // Snapshot of the current ShortCode → Id map. May be empty during cold
    // start (initial RefreshAsync hasn't run yet) or after a refresh failure
    // — callers that can't tolerate that should fall back to a direct DB
    // load and treat the empty map as "cache miss," not "no record types
    // exist."
    IReadOnlyDictionary<string, Guid> ShortCodeToId { get; }
}
