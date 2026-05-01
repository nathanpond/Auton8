using System.Reflection;
using System.Runtime.Loader;

namespace AutoNate.Web.Plugins;

// Per-plugin collectible ALC. Critical detail: assemblies that the host AND
// plugins both reference (notably AutoNate.Plugins.Abstractions, which carries
// IAutoNatePlugin / IHookRegistrar) MUST resolve to the host's copy, otherwise
// the cast `(IAutoNatePlugin)pluginInstance` fails — the plugin's IAutoNatePlugin
// would be a different Type than the one the host knows.
//
// Returning null from Load() defers to the default ALC (the host); that's how
// we unify the shared types.
internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "AutoNate.Plugin.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        // The plugin data API surface (IPluginDataAccess) returns NpgsqlConnection
        // and uses Dapper extension methods. Both must resolve to the host's
        // copy so type identity holds across ALCs — otherwise a plugin's
        // NpgsqlConnection is a different Type than the host's.
        "Npgsql",
        "Dapper",
    };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string entryAssemblyPath)
        : base($"plugin:{Path.GetFileName(entryAssemblyPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && SharedAssemblies.Contains(name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}
