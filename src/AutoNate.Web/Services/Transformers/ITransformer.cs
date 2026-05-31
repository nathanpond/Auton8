using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Transformers;

// Host-side transformer contract (Phase 4 of the Data Stores plan).
// Identical surface to the plugin-facing IPluginTransformer so the
// PluginTransformerAdapter can wrap a plugin impl into this without a
// boundary type. Plugin abstractions can't reference host types, so the
// surface lives twice — accept the duplication for ABI hygiene.
public interface ITransformer
{
    string Key { get; }

    string DisplayName { get; }

    int InputArity => 1;

    Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default);
}
