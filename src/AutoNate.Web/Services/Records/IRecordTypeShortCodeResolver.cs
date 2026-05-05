namespace AutoNate.Web.Services.Records;

// Synchronous lookup of a record type's short code by its id. Used by the
// signal dispatcher to evaluate per-registration record-type filters at
// dispatch time. Implemented by RecordTypeShortCodeCache.
public interface IRecordTypeShortCodeResolver
{
    bool TryGetShortCode(Guid recordTypeId, out string shortCode);
}
