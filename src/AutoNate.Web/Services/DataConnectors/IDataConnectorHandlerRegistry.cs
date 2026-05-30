using AutoNate.Web.Plugins;

namespace AutoNate.Web.Services.DataConnectors;

// Resolves an IDataConnectorHandler by its Kind string. Built-in handlers
// are DI-registered as singletons (REST, SMB stub); plugin-contributed
// handlers come through IPluginConnectorRegistry and are wrapped in
// PluginDataConnectorAdapter at the boundary so consumers see a uniform
// IDataConnectorHandler surface.
public interface IDataConnectorHandlerRegistry
{
    IReadOnlyList<string> Kinds { get; }

    bool TryGet(string kind, out IDataConnectorHandler handler);
}

public sealed class DataConnectorHandlerRegistry : IDataConnectorHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IDataConnectorHandler> _builtIns;
    private readonly IPluginConnectorRegistry? _pluginRegistry;

    public DataConnectorHandlerRegistry(
        IEnumerable<IDataConnectorHandler> handlers,
        IPluginConnectorRegistry? pluginRegistry = null)
    {
        _builtIns = handlers.ToDictionary(h => h.Kind, StringComparer.OrdinalIgnoreCase);
        _pluginRegistry = pluginRegistry;
    }

    // Built-in kinds are stable across the process; plugin kinds change as
    // plugins enable/disable, so callers needing a live list should refetch.
    public IReadOnlyList<string> Kinds
    {
        get
        {
            var keys = new HashSet<string>(_builtIns.Keys, StringComparer.OrdinalIgnoreCase);
            if (_pluginRegistry is not null)
            {
                foreach (var plugin in _pluginRegistry.AllRegistered)
                {
                    keys.Add(plugin.Kind);
                }
            }
            return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    public bool TryGet(string kind, out IDataConnectorHandler handler)
    {
        if (_builtIns.TryGetValue(kind, out var found))
        {
            handler = found;
            return true;
        }
        if (_pluginRegistry is not null && _pluginRegistry.TryGet(kind, out var pluginConnector))
        {
            handler = new PluginDataConnectorAdapter(pluginConnector);
            return true;
        }
        handler = null!;
        return false;
    }
}
