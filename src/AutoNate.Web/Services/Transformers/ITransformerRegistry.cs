using AutoNate.Web.Plugins;

namespace AutoNate.Web.Services.Transformers;

// Resolves an ITransformer by Key. Built-in transformers are DI-registered
// as singletons; plugin-contributed transformers come through
// IPluginTransformerRegistry and are wrapped in PluginTransformerAdapter
// at the registry boundary so consumers see a uniform ITransformer surface.
// Mirrors DataConnectorHandlerRegistry from Phase 1.
public interface ITransformerRegistry
{
    IReadOnlyList<string> Keys { get; }

    IReadOnlyList<ITransformer> All { get; }

    bool TryGet(string key, out ITransformer transformer);
}

public sealed class TransformerRegistry : ITransformerRegistry
{
    private readonly IReadOnlyDictionary<string, ITransformer> _builtIns;
    private readonly IPluginTransformerRegistry? _pluginRegistry;

    public TransformerRegistry(
        IEnumerable<ITransformer> builtIns,
        IPluginTransformerRegistry? pluginRegistry = null)
    {
        _builtIns = builtIns.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        _pluginRegistry = pluginRegistry;
    }

    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new HashSet<string>(_builtIns.Keys, StringComparer.OrdinalIgnoreCase);
            if (_pluginRegistry is not null)
            {
                foreach (var p in _pluginRegistry.AllRegistered) keys.Add(p.Key);
            }
            return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    public IReadOnlyList<ITransformer> All
    {
        get
        {
            var list = new List<ITransformer>(_builtIns.Values);
            if (_pluginRegistry is not null)
            {
                foreach (var p in _pluginRegistry.AllRegistered)
                {
                    list.Add(new PluginTransformerAdapter(p));
                }
            }
            return list;
        }
    }

    public bool TryGet(string key, out ITransformer transformer)
    {
        if (_builtIns.TryGetValue(key, out var found))
        {
            transformer = found;
            return true;
        }
        if (_pluginRegistry is not null && _pluginRegistry.TryGet(key, out var plugin))
        {
            transformer = new PluginTransformerAdapter(plugin);
            return true;
        }
        transformer = null!;
        return false;
    }
}
