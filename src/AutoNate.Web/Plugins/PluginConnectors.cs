using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Host-side IPluginConnectors. Forwards Register calls to the central
// PluginConnectorRegistry tagged with this plugin's id; PluginRuntime
// invokes RemoveAllForPlugin on disable.
internal sealed class PluginConnectors : IPluginConnectors
{
    private readonly IPluginConnectorRegistry _registry;
    private readonly Guid _pluginId;
    private readonly List<IPluginDataConnector> _accepted = new();
    private readonly object _gate = new();

    public PluginConnectors(IPluginConnectorRegistry registry, Guid pluginId)
    {
        _registry = registry;
        _pluginId = pluginId;
    }

    public void Register(IPluginDataConnector connector)
    {
        ArgumentNullException.ThrowIfNull(connector);
        if (_registry.RegisterFromPlugin(_pluginId, connector))
        {
            lock (_gate)
            {
                _accepted.Add(connector);
            }
        }
    }

    public IReadOnlyList<IPluginDataConnector> Registered
    {
        get
        {
            lock (_gate)
            {
                return _accepted.ToArray();
            }
        }
    }

    public int RemoveAll()
    {
        var removed = _registry.RemoveAllForPlugin(_pluginId);
        lock (_gate)
        {
            _accepted.Clear();
        }
        return removed;
    }
}
