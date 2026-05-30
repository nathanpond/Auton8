using AutoNate.Web.Plugins;

namespace AutoNate.Web.Services.Analyzers;

public interface IAnalyzerRegistry
{
    IReadOnlyList<string> Keys { get; }

    IReadOnlyList<IAnalyzer> All { get; }

    bool TryGet(string key, out IAnalyzer analyzer);
}

public sealed class AnalyzerRegistry : IAnalyzerRegistry
{
    private readonly IReadOnlyDictionary<string, IAnalyzer> _builtIns;
    private readonly IPluginAnalyzerRegistry? _pluginRegistry;

    public AnalyzerRegistry(
        IEnumerable<IAnalyzer> builtIns,
        IPluginAnalyzerRegistry? pluginRegistry = null)
    {
        _builtIns = builtIns.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);
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

    public IReadOnlyList<IAnalyzer> All
    {
        get
        {
            var list = new List<IAnalyzer>(_builtIns.Values);
            if (_pluginRegistry is not null)
            {
                foreach (var p in _pluginRegistry.AllRegistered)
                {
                    list.Add(new PluginAnalyzerAdapter(p));
                }
            }
            return list;
        }
    }

    public bool TryGet(string key, out IAnalyzer analyzer)
    {
        if (_builtIns.TryGetValue(key, out var found))
        {
            analyzer = found;
            return true;
        }
        if (_pluginRegistry is not null && _pluginRegistry.TryGet(key, out var plugin))
        {
            analyzer = new PluginAnalyzerAdapter(plugin);
            return true;
        }
        analyzer = null!;
        return false;
    }
}
