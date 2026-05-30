namespace AutoNate.Plugins.Abstractions;

// Plugin-facing transformer contract (Phase 4 of the Data Stores plan).
// Mirrors the host's ITransformer surface exactly so the
// PluginTransformerAdapter can wrap a plugin impl into the host's registry
// without a type conversion at the call boundary. Same goes for
// IPluginAnalyzer.
//
// `Key` is the machine-friendly identifier the React Flow palette and
// pipeline definitions reference (e.g. "filter-rows", "summary-statistics").
// Must not collide with built-in keys or other plugin-contributed keys;
// the registry rejects duplicates at registration time.
//
// `InputArity` is 1 for most transformers and 2 for joins. Future N-ary
// transformers (union, n-way merge) should bump this; the orchestrator
// will pass the corresponding number of inputs.
public interface IPluginTransformer
{
    string Key { get; }

    string DisplayName { get; }

    int InputArity => 1;

    // Config is a flat string→string map (JSON-friendly, easy to author in
    // a React Flow node form). Each transformer documents its own keys.
    Task<DataFrame> RunAsync(
        IReadOnlyList<DataFrame> inputs,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default);
}

public interface IPluginAnalyzer
{
    string Key { get; }

    string DisplayName { get; }

    Task<DataFrame> RunAsync(
        DataFrame input,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default);
}
