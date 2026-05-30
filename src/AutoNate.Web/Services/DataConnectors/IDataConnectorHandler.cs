using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.DataConnectors;

// Per-kind runtime behavior. Built-in REST and SMB handlers are registered
// in the host; plugin-contributed handlers register through
// `IPluginConnectors` (lands later in Phase 1). The registry is keyed by
// `Kind` strings — plugins MUST pick a key that doesn't collide with
// existing handlers.
public interface IDataConnectorHandler
{
    string Kind { get; }

    Task<ConnectorTestResult> TestAsync(
        DataConnector connector,
        CancellationToken cancellationToken = default);

    // Pull data since the supplied refresh state. Returns updated state
    // the caller persists. The fetched rows are streamed into a sink the
    // caller owns — Phase 1 wires this to a TODO sink; Phase 5 (pipelines)
    // wires it to the orchestrator's staging area.
    Task<ConnectorRefreshState> FetchAsync(
        DataConnector connector,
        ConnectorRefreshState state,
        IConnectorFetchSink sink,
        CancellationToken cancellationToken = default);
}

// Forward-declared sink that connector handlers write into during a fetch.
// Real implementation lands alongside the dataset/pipeline storage in
// Phase 2/5; v1 connector code targets this interface so the handler
// surface doesn't churn when the consumer materializes.
public interface IConnectorFetchSink
{
    Task WriteRowAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default);

    Task WriteBlobAsync(string filename, Stream content, CancellationToken cancellationToken = default);
}
