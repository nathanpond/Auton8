using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.DataConnectors.Builtin;

// Built-in REST connector. v1 supports GET with bearer / basic / api-key
// auth modes and `{lastFetchDate}` token interpolation in the URL for
// incremental fetches. Response body is assumed JSON; the configured
// `RowsPath` ($.data / $.items / null) selects the array to stream.
public sealed class RestDataConnectorHandler(
    IHttpClientFactory httpClientFactory,
    IOutboundUrlGuard urlGuard,
    IHostEnvironment environment,
    ILogger<RestDataConnectorHandler> log) : IDataConnectorHandler
{
    public string Kind => DataConnectorKinds.Rest;

    public async Task<ConnectorTestResult> TestAsync(
        DataConnector connector, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var config = ParseConfig(connector);
            var uri = await ResolveAndGuardAsync(config, lastFetchedAtUtc: null, cancellationToken);
            using var client = httpClientFactory.CreateClient("data-connector");
            using var request = BuildRequest(config, uri);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            sw.Stop();
            if (!response.IsSuccessStatusCode)
            {
                return ConnectorTestResult.Fail(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    sw.Elapsed);
            }
            return ConnectorTestResult.Ok(
                $"HTTP {(int)response.StatusCode} reached {request.RequestUri?.Host}",
                sw.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return ConnectorTestResult.Fail($"Request failed: {ex.Message}", sw.Elapsed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or ArgumentException)
        {
            sw.Stop();
            return ConnectorTestResult.Fail($"Configuration error: {ex.Message}", sw.Elapsed);
        }
    }

    public async Task<ConnectorRefreshState> FetchAsync(
        DataConnector connector,
        ConnectorRefreshState state,
        IConnectorFetchSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sink);

        var config = ParseConfig(connector);
        var uri = await ResolveAndGuardAsync(config, state.LastFetchedAtUtc, cancellationToken);
        using var client = httpClientFactory.CreateClient("data-connector");
        using var request = BuildRequest(config, uri);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var rows = SelectRows(document.RootElement, config.RowsPath);

        var fetchedAt = DateTimeOffset.UtcNow;
        var count = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sink.WriteRowAsync(FlattenObject(row), cancellationToken);
            count++;
        }
        log.LogInformation(
            "REST connector {Id} fetched {Count} rows in {Elapsed}ms.",
            connector.Id, count, (DateTimeOffset.UtcNow - fetchedAt).TotalMilliseconds);

        return new ConnectorRefreshState(fetchedAt, null);
    }

    private static RestConnectorConfig ParseConfig(DataConnector connector)
    {
        try
        {
            return JsonSerializer.Deserialize<RestConnectorConfig>(connector.ConfigJson)
                ?? throw new InvalidOperationException("REST config is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("REST connector config is not valid JSON: " + ex.Message);
        }
    }

    // The connector URL is user-supplied config, and the reply body is parsed
    // into rows the caller sees, so an unguarded fetch reads cloud-metadata
    // credentials and internal-only APIs straight out through the app (#60).
    // The set of legitimate destinations is open-ended here — calling
    // third-party APIs is the whole feature — so this is the private-address
    // guard rather than an allowlist. Runs before any socket opens.
    private async Task<Uri> ResolveAndGuardAsync(
        RestConnectorConfig config, DateTimeOffset? lastFetchedAtUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Url))
            throw new InvalidOperationException("REST connector config is missing Url.");

        var token = lastFetchedAtUtc?.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                    ?? string.Empty;
        var resolvedUrl = config.Url.Replace("{lastFetchDate}", token, StringComparison.Ordinal);

        var check = await urlGuard.CheckAsync(
            resolvedUrl, OutboundUrlPolicies.ForEnvironment(environment), cancellationToken);
        if (!check.Allowed)
        {
            throw new InvalidOperationException($"REST connector URL rejected: {check.Error}");
        }
        return check.Uri!;
    }

    private static HttpRequestMessage BuildRequest(RestConnectorConfig config, Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        switch (config.AuthMode?.ToLowerInvariant())
        {
            case "bearer":
                if (!string.IsNullOrWhiteSpace(config.Token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
                break;
            case "basic":
                if (!string.IsNullOrWhiteSpace(config.Username))
                {
                    var raw = (config.Username ?? "") + ":" + (config.Password ?? "");
                    var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
                }
                break;
            case "apikey":
                if (!string.IsNullOrWhiteSpace(config.ApiKeyHeader) && !string.IsNullOrWhiteSpace(config.ApiKey))
                    request.Headers.TryAddWithoutValidation(config.ApiKeyHeader, config.ApiKey);
                break;
        }
        return request;
    }

    private static IEnumerable<JsonElement> SelectRows(JsonElement root, string? rowsPath)
    {
        var target = root;
        if (!string.IsNullOrWhiteSpace(rowsPath) && rowsPath != "$")
        {
            // Tiny JSONPath subset: $.a.b
            var path = rowsPath.TrimStart('$').TrimStart('.');
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(segment, out var next))
                    yield break;
                target = next;
            }
        }
        if (target.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in target.EnumerateArray())
        {
            yield return item;
        }
    }

    private static IReadOnlyDictionary<string, object?> FlattenObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var i) ? i : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Object or JsonValueKind.Array => prop.Value.GetRawText(),
                _ => prop.Value.GetRawText()
            };
        }
        return dict;
    }
}
