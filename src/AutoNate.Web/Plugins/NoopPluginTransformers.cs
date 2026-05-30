using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

internal sealed class NoopPluginTransformers : IPluginTransformers
{
    public void Register(IPluginTransformer transformer) { }
    public IReadOnlyList<IPluginTransformer> Registered => Array.Empty<IPluginTransformer>();
    public int RemoveAll() => 0;
}

internal sealed class NoopPluginAnalyzers : IPluginAnalyzers
{
    public void Register(IPluginAnalyzer analyzer) { }
    public IReadOnlyList<IPluginAnalyzer> Registered => Array.Empty<IPluginAnalyzer>();
    public int RemoveAll() => 0;
}
