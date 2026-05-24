using AutoNate.Web.Services.Projections.Feeds;
using AutoNate.Web.Services.Projections.Stores;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoNate.Web.Services.Projections;

public static class ProjectionServiceCollectionExtensions
{
    // Wires up the projection framework: registry, worker, version store,
    // watermark store, plus a default Postgres store. Call once from Program.cs
    // before AddProjection<...>() calls.
    public static IServiceCollection AddProjectionFramework(this IServiceCollection services)
    {
        services.TryAddSingleton<IProjectionRegistry, ProjectionRegistry>();
        services.TryAddSingleton<IProjectionVersionStore, PostgresProjectionVersionStore>();
        services.TryAddSingleton<IProjectionWatermarkStore, PostgresProjectionWatermarkStore>();
        services.TryAddSingleton<IProjectionHealthService, ProjectionHealthService>();
        services.TryAddSingleton<BackfillRunner>();
        services.AddHostedService<ProjectionWorker>();
        return services;
    }

    // Registers a projection with the framework. The projection becomes
    // discoverable to the worker via both the typed and untyped IProjection
    // interface; the worker resolves IEnumerable<IChangeFeed<TSource>> at
    // start to find every feed that targets the same source.
    public static IServiceCollection AddProjection<TSource, TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection<TSource>
    {
        services.AddSingleton<TProjection>();
        services.AddSingleton<IProjection>(sp => sp.GetRequiredService<TProjection>());
        services.AddSingleton<IProjection<TSource>>(sp => sp.GetRequiredService<TProjection>());
        return services;
    }

    public static IServiceCollection AddChangeFeed<TSource, TFeed>(this IServiceCollection services)
        where TFeed : class, IChangeFeed<TSource>
    {
        services.AddSingleton<TFeed>();
        services.AddSingleton<IChangeFeed<TSource>>(sp => sp.GetRequiredService<TFeed>());
        return services;
    }

    // Manual feed is generic; only register if the consumer actually wants
    // direct enqueue access for a specific TSource (most useful in tests).
    public static IServiceCollection AddManualChangeFeed<TSource>(this IServiceCollection services)
    {
        services.AddSingleton<ManualChangeFeed<TSource>>(_ => new ManualChangeFeed<TSource>("manual"));
        services.AddSingleton<IChangeFeed<TSource>>(sp => sp.GetRequiredService<ManualChangeFeed<TSource>>());
        return services;
    }
}
