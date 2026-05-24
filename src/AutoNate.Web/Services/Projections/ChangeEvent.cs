namespace AutoNate.Web.Services.Projections;

public enum ChangeOp
{
    // Insert-or-update by SourceId. Source must be non-null.
    Upsert = 1,

    // Tombstone by SourceId. Source may be null (we only need the key).
    Delete = 2
}

// A single change observed on a projection source. The framework's contract:
// projections never reorder events within a SourceId — successive Upserts
// for the same key collapse to "latest wins" inside one batch, but a Delete
// followed by an Upsert (or vice versa) is preserved in order.
public sealed record ChangeEvent<TSource>(
    ChangeOp Op,
    string SourceId,
    TSource? Source,
    DateTimeOffset ObservedAt);
