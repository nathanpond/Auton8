namespace AutoNate.Web.Services.DataConnectors;

// Per-connector incremental-fetch bookmark. Handlers read this on each
// fetch and return an updated copy that the store persists. `Cursor` is
// opaque to the host — REST handlers store last-fetch ISO timestamps,
// SMB handlers store file mtimes / hashes, plugin handlers store whatever
// their backend needs. Null cursor = first fetch (full pull).
public sealed record class ConnectorRefreshState(
    DateTimeOffset? LastFetchedAtUtc,
    string? Cursor);
