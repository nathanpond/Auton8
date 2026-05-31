using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.DataConnectors;

// Adapts an IPluginDataConnector (from AutoNate.Plugin.Abstractions, with
// plugin-safe context types) to the host's IDataConnectorHandler surface
// (which speaks the EF-backed DataConnector entity + host fetch sink).
// Lives in the host so the abstractions package stays free of any
// Persistence.Scaffolded coupling.
internal sealed class PluginDataConnectorAdapter(IPluginDataConnector inner) : IDataConnectorHandler
{
    public string Kind => inner.Kind;

    public async Task<ConnectorTestResult> TestAsync(
        DataConnector connector, CancellationToken cancellationToken = default)
    {
        var pluginContext = new PluginConnectorContext(
            connector.Id, connector.Name, connector.Kind, connector.ConfigJson);
        var result = await inner.TestAsync(pluginContext, cancellationToken);
        return new ConnectorTestResult(result.Success, result.Message, result.Elapsed);
    }

    public async Task<ConnectorRefreshState> FetchAsync(
        DataConnector connector,
        ConnectorRefreshState state,
        IConnectorFetchSink sink,
        CancellationToken cancellationToken = default)
    {
        var pluginContext = new PluginConnectorContext(
            connector.Id, connector.Name, connector.Kind, connector.ConfigJson);
        var pluginState = new PluginConnectorRefreshState(state.LastFetchedAtUtc, state.Cursor);
        var pluginSink = new SinkAdapter(sink);
        var result = await inner.FetchAsync(pluginContext, pluginState, pluginSink, cancellationToken);
        return new ConnectorRefreshState(result.LastFetchedAtUtc, result.Cursor);
    }

    private sealed class SinkAdapter(IConnectorFetchSink inner) : IPluginConnectorFetchSink
    {
        public Task WriteRowAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
            => inner.WriteRowAsync(row, cancellationToken);

        public Task WriteBlobAsync(string filename, Stream content, CancellationToken cancellationToken = default)
            => inner.WriteBlobAsync(filename, content, cancellationToken);
    }
}
