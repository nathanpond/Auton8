namespace AutoNate.Plugins.Abstractions;

// Plugin-facing surface for contributing a DataConnector kind to the host
// (docs/plans/2026-05-30-data-stores-implementation.md). Plugins register
// implementations through IPluginConnectors during Configure(); the host
// adapts each to its internal IDataConnectorHandler and routes
// `dataconnectors.kind = <Kind>` to it.
//
// Lifecycle: removal happens synchronously on plugin disable, mirroring
// IPluginBehaviors. In-flight FetchAsync calls run to completion in the
// plugin's AssemblyLoadContext; new ones after disable surface as a 404
// from the connector test/fetch endpoints.
public interface IPluginDataConnector
{
    // Stable string key identifying this kind. Must not collide with built-in
    // host kinds (`rest`, `smb`) or kinds registered by other plugins. The
    // host's registry throws on duplicate registration at Configure time.
    string Kind { get; }

    // Probe the configured endpoint without writing any data. Surfaced as the
    // "Test connection" button on the admin form.
    Task<PluginConnectorTestResult> TestAsync(
        PluginConnectorContext context,
        CancellationToken cancellationToken = default);

    // Pull data since the supplied refresh state. The returned refresh state
    // is persisted by the host; the next call sees it back. Rows go into the
    // sink — pipeline orchestration owns where they land.
    Task<PluginConnectorRefreshState> FetchAsync(
        PluginConnectorContext context,
        PluginConnectorRefreshState state,
        IPluginConnectorFetchSink sink,
        CancellationToken cancellationToken = default);
}

// What plugin handlers see — opaque ConfigJson + the prior refresh bookmark.
public sealed record class PluginConnectorContext(
    Guid Id,
    string Name,
    string Kind,
    string ConfigJson);

public sealed record class PluginConnectorRefreshState(
    DateTimeOffset? LastFetchedAtUtc,
    string? Cursor);

public sealed record class PluginConnectorTestResult(
    bool Success,
    string Message,
    TimeSpan Elapsed)
{
    public static PluginConnectorTestResult Ok(string message, TimeSpan elapsed)
        => new(true, message, elapsed);

    public static PluginConnectorTestResult Fail(string message, TimeSpan elapsed)
        => new(false, message, elapsed);
}

// Host-owned sink the plugin writes fetched rows / blobs into. The host
// decides what to do with them (cache table, staging area, pipeline input).
public interface IPluginConnectorFetchSink
{
    Task WriteRowAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default);

    Task WriteBlobAsync(string filename, Stream content, CancellationToken cancellationToken = default);
}
