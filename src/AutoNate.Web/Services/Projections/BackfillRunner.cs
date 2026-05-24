using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Projections;

// Drives a one-shot full reprojection of a cache: streams every source row
// through IProjectionBackfillSource and hands them to the projection in
// chunks. Each chunk gets the same ApplyAsync contract the live worker uses,
// so behavior is identical between the backfill path and the streaming
// path.
//
// The shadow-table-then-rename swap described in the design doc is not yet
// implemented — this runner writes through to the active table so a fresh
// install can populate without a live source. The shadow-rename path will
// land when the first version bump is needed in anger.
public sealed class BackfillRunner
{
    private readonly IServiceProvider _services;
    private readonly IProjectionRegistry _registry;
    private readonly IProjectionVersionStore _versions;
    private readonly ILogger<BackfillRunner> _logger;

    public BackfillRunner(
        IServiceProvider services,
        IProjectionRegistry registry,
        IProjectionVersionStore versions,
        ILogger<BackfillRunner> logger)
    {
        _services = services;
        _registry = registry;
        _versions = versions;
        _logger = logger;
    }

    public async Task<int> RunAsync(string projectionName, int chunkSize = 500, CancellationToken cancellationToken = default)
    {
        var projection = _registry.TryGet(projectionName)
            ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered.");

        // NonPublic by design — RunGenericAsync is the internal generic-method
        // dispatch path; promoting it to public would surface an API the
        // framework's own consumers wouldn't usefully call.
#pragma warning disable S3011 // intentional reflection: private generic dispatch
        var runMethod = typeof(BackfillRunner)
            .GetMethod(nameof(RunGenericAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(projection.SourceType);
#pragma warning restore S3011

        var task = (Task<int>)runMethod.Invoke(this, new object[] { projection, chunkSize, cancellationToken })!;
        return await task;
    }

    private async Task<int> RunGenericAsync<TSource>(
        IProjection<TSource> projection,
        int chunkSize,
        CancellationToken cancellationToken)
    {
        var source = _services.GetService<IProjectionBackfillSource<TSource>>();
        if (source is null)
        {
            throw new InvalidOperationException(
                $"No IProjectionBackfillSource<{typeof(TSource).Name}> registered for projection '{projection.Name}'.");
        }

        await _versions.SetActiveAsync(projection.Name, projection.Version, cancellationToken);

        var dbFactory = _services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var buffer = new List<ChangeEvent<TSource>>(chunkSize);
        var total = 0;

        await foreach (var change in source.EnumerateAllAsync(cancellationToken))
        {
            buffer.Add(change);
            if (buffer.Count >= chunkSize)
            {
                total += await FlushAsync(projection, dbFactory, buffer, cancellationToken);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            total += await FlushAsync(projection, dbFactory, buffer, cancellationToken);
        }

        _logger.LogInformation(
            "Backfill of projection {Name} (version {Version}) wrote {Total} rows.",
            projection.Name, projection.Version, total);
        return total;
    }

    private static async Task<int> FlushAsync<TSource>(
        IProjection<TSource> projection,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IReadOnlyList<ChangeEvent<TSource>> batch,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await projection.ApplyAsync(batch, db, cancellationToken);
        return batch.Count;
    }
}
