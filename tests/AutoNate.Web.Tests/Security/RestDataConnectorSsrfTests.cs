using System.Text.Json;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.DataConnectors;
using AutoNate.Web.Services.DataConnectors.Builtin;
using AutoNate.Web.Services.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// archived-60: `POST /api/dataconnectors/{id}/preview` fetched the connector's
// configured URL with no guard and returned the parsed body as rows, so a user
// with DataConnector:Create + Connect could read cloud instance credentials and
// internal-only services through the app.
//
// The handler under test is wired with an HTTP handler that fails the test if
// it is ever invoked: the point is that the request is refused *before a socket
// opens*, not that it fails somewhere downstream.
public sealed class RestDataConnectorSsrfTests
{
    [Theory]
    [InlineData("http://169.254.169.254/computeMetadata/v1/instance/service-accounts/default/token")]
    [InlineData("http://127.0.0.1:8222/varz")]                  // NATS monitoring
    [InlineData("http://localhost:3500/v1.0/secrets")]           // Dapr sidecar
    [InlineData("http://10.1.2.3/internal")]
    [InlineData("http://[::1]:8080/flowable-rest")]
    public async Task Fetch_against_an_internal_address_is_refused_before_any_request(string url)
    {
        var handler = CreateHandler(url, out var http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.FetchAsync(Connector(url), new ConnectorRefreshState(null, null), new ThrowingSink()));

        Assert.Contains("REST connector URL rejected", ex.Message);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task Test_against_an_internal_address_reports_failure_without_calling_out()
    {
        const string url = "http://169.254.169.254/latest/meta-data/";
        var handler = CreateHandler(url, out var http);

        var result = await handler.TestAsync(Connector(url));

        Assert.False(result.Success);
        Assert.Contains("rejected", result.Message);
        Assert.Equal(0, http.Calls);
    }

    // `{lastFetchDate}` is interpolated into the URL before the fetch, so the
    // guard has to run on the interpolated value, not the template.
    [Fact]
    public async Task Guard_runs_on_the_interpolated_url()
    {
        const string template = "http://169.254.169.254/x?since={lastFetchDate}";
        var handler = CreateHandler(template, out var http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.FetchAsync(
                Connector(template),
                new ConnectorRefreshState(DateTimeOffset.UtcNow, null),
                new ThrowingSink()));

        Assert.Contains("REST connector URL rejected", ex.Message);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task Plain_http_is_refused_outside_development()
    {
        const string url = "http://api.example.com/rows";
        var handler = CreateHandler(url, out var http, environmentName: "Production");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.FetchAsync(Connector(url), new ConnectorRefreshState(null, null), new ThrowingSink()));

        Assert.Contains("https is required", ex.Message);
        Assert.Equal(0, http.Calls);
    }

    // The guard must not break the feature: a public https endpoint still works.
    [Fact]
    public async Task A_public_https_endpoint_still_fetches_rows()
    {
        const string url = "https://api.example.com/rows";
        var handler = CreateHandler(
            url,
            out var http,
            responseJson: """[{"id":1,"name":"alpha"},{"id":2,"name":"beta"}]""");

        var sink = new CollectingSink();
        await handler.FetchAsync(Connector(url), new ConnectorRefreshState(null, null), sink);

        Assert.Equal(1, http.Calls);
        Assert.Equal(2, sink.Rows.Count);
        Assert.Equal("alpha", sink.Rows[0]["name"]);
    }

    // archived-165: the handler bound ConfigJson case-sensitively while the SPA writes
    // and documents camelCase, so every UI-authored REST connector resolved to
    // an empty Url and failed with "config is missing Url" — the built-in
    // connector could not be configured through its own admin page.
    [Theory]
    [InlineData("""{"url":"https://api.example.com/rows","authMode":"none"}""")]
    [InlineData("""{"Url":"https://api.example.com/rows","AuthMode":"none"}""")]
    public async Task Config_binds_in_either_casing(string configJson)
    {
        var http = new CountingHandler("""[{"id":1,"name":"alpha"}]""");
        var factory = new SingleClientFactory(new HttpClient(http));
        var guard = new OutboundUrlGuard(OutboundUrlGuardTests.FakeDns.Of("api.example.com", "93.184.216.34"));
        var handler = new RestDataConnectorHandler(
            factory, guard, new StubEnvironment("Development"),
            NullLogger<RestDataConnectorHandler>.Instance);

        var connector = new DataConnector
        {
            Id = Guid.NewGuid(),
            Name = "probe",
            Kind = DataConnectorKinds.Rest,
            ConfigJson = configJson,
        };

        var sink = new CollectingSink();
        await handler.FetchAsync(connector, new ConnectorRefreshState(null, null), sink);

        Assert.Equal(1, http.Calls);
        Assert.Single(sink.Rows);
    }

    private static RestDataConnectorHandler CreateHandler(
        string url,
        out CountingHandler http,
        string environmentName = "Development",
        string responseJson = "[]")
    {
        http = new CountingHandler(responseJson);
        var factory = new SingleClientFactory(new HttpClient(http));
        // Public DNS answer for api.example.com; anything else in these tests is
        // an IP literal, which never reaches the resolver.
        var guard = new OutboundUrlGuard(OutboundUrlGuardTests.FakeDns.Of("api.example.com", "93.184.216.34"));
        return new RestDataConnectorHandler(
            factory, guard, new StubEnvironment(environmentName),
            NullLogger<RestDataConnectorHandler>.Instance);
    }

    // camelCase, i.e. exactly what the SPA writes
    // ({"url": "", "authMode": "none"} — DataConnectorsPage.tsx). It only
    // binds since archived-165; before that these tests had to use PascalCase, which
    // meant they never exercised a config shape any real connector had.
    private static DataConnector Connector(string url) => new()
    {
        Id = Guid.NewGuid(),
        Name = "probe",
        Kind = DataConnectorKinds.Rest,
        ConfigJson = JsonSerializer.Serialize(new { url, authMode = "none" }),
    };

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _json;
        public int Calls;

        public CountingHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class ThrowingSink : IConnectorFetchSink
    {
        public Task WriteRowAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A refused fetch must not write rows.");

        public Task WriteBlobAsync(string filename, Stream content, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A refused fetch must not write blobs.");
    }

    private sealed class CollectingSink : IConnectorFetchSink
    {
        public List<IReadOnlyDictionary<string, object?>> Rows { get; } = [];

        public Task WriteRowAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }

        public Task WriteBlobAsync(string filename, Stream content, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public StubEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
