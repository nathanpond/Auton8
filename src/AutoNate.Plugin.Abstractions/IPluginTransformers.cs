namespace AutoNate.Plugins.Abstractions;

// Plugin-facing helper for contributing IPluginTransformer / IPluginAnalyzer
// implementations to the host's transformer and analyzer registries
// (Phase 4 of the Data Stores plan). Mirror IPluginBehaviors /
// IPluginProjections / IPluginConnectors lifecycle: tagged by plugin id,
// auto-removed on disable, re-registered on each Configure().
public interface IPluginTransformers
{
    void Register(IPluginTransformer transformer);

    IReadOnlyList<IPluginTransformer> Registered { get; }

    int RemoveAll();
}

public interface IPluginAnalyzers
{
    void Register(IPluginAnalyzer analyzer);

    IReadOnlyList<IPluginAnalyzer> Registered { get; }

    int RemoveAll();
}
