using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Host-side registry for plugin-contributed IPluginDataConnector
// implementations. Mirrors IWorkflowBehaviorRegistry exactly. The
// DataConnectorHandlerRegistry consults this in addition to its
// DI-registered built-in handlers, so plugin-contributed kinds are
// indistinguishable from built-ins at the handler boundary.
public interface IPluginConnectorRegistry
{
    // Returns true if the kind was accepted; false if a duplicate-kind
    // collision was logged and the registration ignored.
    bool RegisterFromPlugin(Guid pluginId, IPluginDataConnector connector);

    int RemoveAllForPlugin(Guid pluginId);

    IReadOnlyList<IPluginDataConnector> AllRegistered { get; }

    bool TryGet(string kind, out IPluginDataConnector connector);
}

internal sealed class PluginConnectorRegistry : IPluginConnectorRegistry
{
    private readonly Dictionary<string, (Guid PluginId, IPluginDataConnector Connector)> _byKind
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool RegisterFromPlugin(Guid pluginId, IPluginDataConnector connector)
    {
        ArgumentNullException.ThrowIfNull(connector);
        lock (_gate)
        {
            if (_byKind.ContainsKey(connector.Kind)) return false;
            _byKind[connector.Kind] = (pluginId, connector);
            return true;
        }
    }

    public int RemoveAllForPlugin(Guid pluginId)
    {
        lock (_gate)
        {
            var doomed = _byKind
                .Where(kv => kv.Value.PluginId == pluginId)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in doomed) _byKind.Remove(key);
            return doomed.Count;
        }
    }

    public IReadOnlyList<IPluginDataConnector> AllRegistered
    {
        get
        {
            lock (_gate)
            {
                return _byKind.Values.Select(v => v.Connector).ToArray();
            }
        }
    }

    public bool TryGet(string kind, out IPluginDataConnector connector)
    {
        lock (_gate)
        {
            if (_byKind.TryGetValue(kind, out var entry))
            {
                connector = entry.Connector;
                return true;
            }
            connector = null!;
            return false;
        }
    }
}
