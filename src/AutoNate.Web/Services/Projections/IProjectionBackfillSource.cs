namespace AutoNate.Web.Services.Projections;

// Implemented by anything that can re-emit every source row from the start —
// e.g. paging Flowable REST for every historic process instance. Used by
// BackfillRunner to seed a fresh cache or rebuild after a version bump.
//
// Implementations should yield in a stable order (typically by id) so that
// long backfills can be resumed after a crash without missing rows.
public interface IProjectionBackfillSource<TSource>
{
    string ProjectionName { get; }

    IAsyncEnumerable<ChangeEvent<TSource>> EnumerateAllAsync(CancellationToken cancellationToken);
}
