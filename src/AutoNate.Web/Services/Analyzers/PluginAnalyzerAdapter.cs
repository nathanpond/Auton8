using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Analyzers;

internal sealed class PluginAnalyzerAdapter(IPluginAnalyzer inner) : IAnalyzer
{
    public string Key => inner.Key;
    public string DisplayName => inner.DisplayName;

    public Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
        => inner.RunAsync(input, config, cancellationToken);
}
