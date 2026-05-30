using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Host-side registries for plugin-contributed IPluginTransformer /
// IPluginAnalyzer implementations. Mirror IPluginConnectorRegistry from
// Phase 1 exactly: tagged-by-pluginId registration, RemoveAllForPlugin on
// disable, duplicate-key registrations rejected.
public interface IPluginTransformerRegistry
{
    bool RegisterFromPlugin(Guid pluginId, IPluginTransformer transformer);

    int RemoveAllForPlugin(Guid pluginId);

    IReadOnlyList<IPluginTransformer> AllRegistered { get; }

    bool TryGet(string key, out IPluginTransformer transformer);
}

internal sealed class PluginTransformerRegistry : IPluginTransformerRegistry
{
    private readonly Dictionary<string, (Guid PluginId, IPluginTransformer Transformer)> _byKey
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool RegisterFromPlugin(Guid pluginId, IPluginTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        lock (_gate)
        {
            if (_byKey.ContainsKey(transformer.Key)) return false;
            _byKey[transformer.Key] = (pluginId, transformer);
            return true;
        }
    }

    public int RemoveAllForPlugin(Guid pluginId)
    {
        lock (_gate)
        {
            var doomed = _byKey.Where(kv => kv.Value.PluginId == pluginId)
                .Select(kv => kv.Key).ToList();
            foreach (var k in doomed) _byKey.Remove(k);
            return doomed.Count;
        }
    }

    public IReadOnlyList<IPluginTransformer> AllRegistered
    {
        get
        {
            lock (_gate)
            {
                return _byKey.Values.Select(v => v.Transformer).ToArray();
            }
        }
    }

    public bool TryGet(string key, out IPluginTransformer transformer)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var entry))
            {
                transformer = entry.Transformer;
                return true;
            }
            transformer = null!;
            return false;
        }
    }
}

public interface IPluginAnalyzerRegistry
{
    bool RegisterFromPlugin(Guid pluginId, IPluginAnalyzer analyzer);

    int RemoveAllForPlugin(Guid pluginId);

    IReadOnlyList<IPluginAnalyzer> AllRegistered { get; }

    bool TryGet(string key, out IPluginAnalyzer analyzer);
}

internal sealed class PluginAnalyzerRegistry : IPluginAnalyzerRegistry
{
    private readonly Dictionary<string, (Guid PluginId, IPluginAnalyzer Analyzer)> _byKey
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool RegisterFromPlugin(Guid pluginId, IPluginAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        lock (_gate)
        {
            if (_byKey.ContainsKey(analyzer.Key)) return false;
            _byKey[analyzer.Key] = (pluginId, analyzer);
            return true;
        }
    }

    public int RemoveAllForPlugin(Guid pluginId)
    {
        lock (_gate)
        {
            var doomed = _byKey.Where(kv => kv.Value.PluginId == pluginId)
                .Select(kv => kv.Key).ToList();
            foreach (var k in doomed) _byKey.Remove(k);
            return doomed.Count;
        }
    }

    public IReadOnlyList<IPluginAnalyzer> AllRegistered
    {
        get
        {
            lock (_gate)
            {
                return _byKey.Values.Select(v => v.Analyzer).ToArray();
            }
        }
    }

    public bool TryGet(string key, out IPluginAnalyzer analyzer)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var entry))
            {
                analyzer = entry.Analyzer;
                return true;
            }
            analyzer = null!;
            return false;
        }
    }
}
