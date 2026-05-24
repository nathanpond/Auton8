using Microsoft.Extensions.Hosting;

namespace AutoNate.Web.Plugins;

// Wrapper around the host's root IServiceProvider that exposes only an
// allowlist of safe types to plugins.
//
// Why: IPluginContext.HostServices used to hand the raw root provider to
// every plugin's Configure(). A plugin running in a collectible ALC could
// then call `HostServices.GetService<IConfiguration>()` and pull every
// connection string, signing key, and `*SharedSecret` out of the host —
// bypassing the per-plugin Postgres role isolation entirely.
//
// Plugins today only ever reach for `ILoggerFactory` (verified across the
// shipped sample plugins and the documentation in PluginDocumentation.tsx).
// We expose that plus a handful of obviously-safe adjacents (typed loggers,
// TimeProvider, the host-environment metadata) and return null for
// everything else. Adding a new type here is a deliberate review step.
internal sealed class SafePluginServiceProvider : IServiceProvider
{
    private static readonly HashSet<Type> AllowedClosedTypes =
    [
        typeof(ILoggerFactory),
        typeof(TimeProvider),
        typeof(IHostEnvironment)
    ];

    private static readonly HashSet<Type> AllowedOpenGenerics =
    [
        typeof(ILogger<>)
    ];

    private readonly IServiceProvider _inner;

    public SafePluginServiceProvider(IServiceProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (AllowedClosedTypes.Contains(serviceType))
        {
            return _inner.GetService(serviceType);
        }

        if (serviceType.IsGenericType
            && AllowedOpenGenerics.Contains(serviceType.GetGenericTypeDefinition()))
        {
            return _inner.GetService(serviceType);
        }

        return null;
    }
}
