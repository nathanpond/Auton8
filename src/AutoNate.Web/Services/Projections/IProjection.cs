using AutoNate.Web.Persistence;

namespace AutoNate.Web.Services.Projections;

// Non-generic facet used by the registry and worker so they can iterate
// projections without knowing the source type up front.
public interface IProjection
{
    string Name { get; }

    // Bumping this triggers a background reprojection into a shadow table.
    // Atomic rename happens once the shadow build reaches the source's
    // current head; the old version drops after a grace period.
    int Version { get; }

    Type SourceType { get; }
}

// Per-source projection. Owns its persistence — receives a batch of change
// events under a shared DbContext and writes them however its target table
// is shaped. The framework only contributes batching, transactions, version
// + watermark bookkeeping, and lifecycle.
//
// Implementations should be idempotent on SourceId: replaying the same
// upsert is a no-op modulo row contents, and replaying a delete after the
// row is gone is also a no-op. This is what makes the push (NATS) and pull
// (sweeper) feeds composable without dedup logic.
public interface IProjection<TSource> : IProjection
{
    Task ApplyAsync(
        IReadOnlyList<ChangeEvent<TSource>> batch,
        AutoNateDbContext db,
        CancellationToken cancellationToken);
}
