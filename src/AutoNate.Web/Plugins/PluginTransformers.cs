using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

internal sealed class PluginTransformers : IPluginTransformers
{
    private readonly IPluginTransformerRegistry _registry;
    private readonly Guid _pluginId;
    private readonly List<IPluginTransformer> _accepted = new();
    private readonly object _gate = new();

    public PluginTransformers(IPluginTransformerRegistry registry, Guid pluginId)
    {
        _registry = registry;
        _pluginId = pluginId;
    }

    public void Register(IPluginTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        if (_registry.RegisterFromPlugin(_pluginId, transformer))
        {
            lock (_gate) { _accepted.Add(transformer); }
        }
    }

    public IReadOnlyList<IPluginTransformer> Registered
    {
        get
        {
            lock (_gate) { return _accepted.ToArray(); }
        }
    }

    public int RemoveAll()
    {
        var removed = _registry.RemoveAllForPlugin(_pluginId);
        lock (_gate) { _accepted.Clear(); }
        return removed;
    }
}

internal sealed class PluginAnalyzers : IPluginAnalyzers
{
    private readonly IPluginAnalyzerRegistry _registry;
    private readonly Guid _pluginId;
    private readonly List<IPluginAnalyzer> _accepted = new();
    private readonly object _gate = new();

    public PluginAnalyzers(IPluginAnalyzerRegistry registry, Guid pluginId)
    {
        _registry = registry;
        _pluginId = pluginId;
    }

    public void Register(IPluginAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        if (_registry.RegisterFromPlugin(_pluginId, analyzer))
        {
            lock (_gate) { _accepted.Add(analyzer); }
        }
    }

    public IReadOnlyList<IPluginAnalyzer> Registered
    {
        get
        {
            lock (_gate) { return _accepted.ToArray(); }
        }
    }

    public int RemoveAll()
    {
        var removed = _registry.RemoveAllForPlugin(_pluginId);
        lock (_gate) { _accepted.Clear(); }
        return removed;
    }
}
