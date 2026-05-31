namespace AutoNate.Web.Services.DataConnectors.Builtin;

// JSON shape stored in dataconnectors.config for the built-in REST kind.
// The URL may contain the literal token `{lastFetchDate}` (ISO 8601 UTC)
// for incremental fetches — null on the first run, otherwise the previous
// run's last_fetched_at_utc. Plugins use their own config shapes; nothing
// reads this outside RestDataConnectorHandler.
public sealed class RestConnectorConfig
{
    public string Url { get; set; } = "";

    // "none" | "bearer" | "basic" | "apiKey"
    public string AuthMode { get; set; } = "none";

    // bearer = Authorization: Bearer <Token>
    public string? Token { get; set; }

    // basic = Authorization: Basic base64(Username:Password)
    public string? Username { get; set; }

    public string? Password { get; set; }

    // apiKey = HTTP header `<ApiKeyHeader>: <ApiKey>`
    public string? ApiKeyHeader { get; set; }

    public string? ApiKey { get; set; }

    // Optional JSONPath-ish hint (root, $.data, $.items) telling the handler
    // where rows live in the response body. Null = response body is a JSON
    // array at the root.
    public string? RowsPath { get; set; }
}
