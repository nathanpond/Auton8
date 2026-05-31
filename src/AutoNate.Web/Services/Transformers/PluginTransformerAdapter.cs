using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers;

// Wraps an IPluginTransformer so it satisfies the host-side ITransformer
// surface. Currently a one-to-one passthrough — both interfaces speak
// DataFrame from the abstractions package, so no marshalling is needed.
// The wrapper exists so consumers (TransformerRegistry, endpoints, the
// pipeline orchestrator in Phase 5) deal with a single type.
internal sealed class PluginTransformerAdapter(IPluginTransformer inner) : ITransformer
{
    public string Key => inner.Key;
    public string DisplayName => inner.DisplayName;
    public int InputArity => inner.InputArity;

    public Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
        => inner.RunAsync(inputs, config, cancellationToken);
}
