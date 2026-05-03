using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoNate.Web.Configuration;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Dapr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace AutoNate.Web.Services.SystemHealth;

// Serialized by name (e.g. "Up") rather than the default integer ordinal so
// the SPA can switch on string literals without a separate mapping table.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthStatus
{
    Up,
    Down,
    Degraded,
    Unknown
}

public sealed record ComponentHealth(
    string Id,
    string Name,
    string Kind,
    HealthStatus Status,
    string? Message,
    Dictionary<string, string>? Details,
    int? LatencyMs);

public sealed record ConnectionHealth(
    string From,
    string To,
    string Label,
    HealthStatus Status,
    string? Message,
    int? LatencyMs);

public sealed record SystemHealthReport(
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<ComponentHealth> Components,
    IReadOnlyList<ConnectionHealth> Connections);

// Tiny probe contract so detectors and other consumers can depend on the
// abstract probe instead of the concrete (sealed) SystemHealthService.
public interface ISystemHealthProbe
{
    Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default);
}

// Probes every external dependency and reports both component-level health
// (is each service alive?) and connection-level health (is each expected
// edge between services actually working?). Used by the SPA's System Health
// admin page to surface broken links — most importantly the autonate-web
// Dapr sidecar's NATS connection, which is silent when it dies (subscribe
// calls succeed at the SDK layer while no JetStream consumer is registered).
public sealed class SystemHealthService(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    DaprSidecarProbe daprSidecarProbe,
    IOptions<DaprOptions> daprOptions,
    IOptions<NatsOptions> natsOptions,
    IOptions<FlowableOptions> flowableOptions,
    ILogger<SystemHealthService> logger) : ISystemHealthProbe
{
    private readonly DaprOptions _daprOptions = daprOptions.Value;
    private readonly NatsOptions _natsOptions = natsOptions.Value;
    private readonly FlowableOptions _flowableOptions = flowableOptions.Value;

    public async Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var components = new List<ComponentHealth>();
        var connections = new List<ConnectionHealth>();

        // Self — by definition this code is running, so the web app is up.
        // Latency is omitted because there's nothing to measure.
        components.Add(new ComponentHealth(
            Id: "autonate-web",
            Name: "AutoNate.Web",
            Kind: "service",
            Status: HealthStatus.Up,
            Message: "Responding to HTTP requests",
            Details: null,
            LatencyMs: null));

        var (postgresComponent, postgresConnection) = await CheckPostgresAsync(cancellationToken);
        components.Add(postgresComponent);
        connections.Add(postgresConnection);

        var (daprHttpComponent, daprHttpConnection) = await CheckDaprHttpAsync(cancellationToken);
        components.Add(daprHttpComponent);
        connections.Add(daprHttpConnection);

        // Dapr's pub/sub component is a logical node. Failing here is the
        // signal that the sidecar's NATS connection died (the symptom is
        // "nats: connection closed" returned from a publish probe).
        var (pubsubComponent, pubsubConnection) = await CheckDaprPubSubAsync(daprHttpComponent.Status, cancellationToken);
        components.Add(pubsubComponent);
        connections.Add(pubsubConnection);

        var (natsComponent, natsConnection) = await CheckNatsAsync(cancellationToken);
        components.Add(natsComponent);
        connections.Add(natsConnection);

        // The expected edge from Dapr pub/sub down to NATS — separate from
        // direct AutoNate→NATS so the user can tell which leg is broken.
        connections.Add(BuildPubSubToNatsEdge(pubsubComponent, natsComponent));

        var redisChecks = await CheckRedisStateAsync(daprHttpComponent.Status, cancellationToken);
        components.Add(redisChecks.Component);
        connections.Add(redisChecks.AppToState);
        connections.Add(redisChecks.StateToRedis);

        var (flowableComponent, flowableConnection) = await CheckFlowableAsync(cancellationToken);
        components.Add(flowableComponent);
        connections.Add(flowableConnection);

        var (placementComponent, placementConnection) = await CheckDaprControlPlaneAsync(
            id: "dapr-placement",
            name: "Dapr Placement",
            connectionLabel: "Actor placement",
            hostAddress: _daprOptions.PlacementHostAddress,
            cancellationToken);
        components.Add(placementComponent);
        connections.Add(placementConnection);

        var (schedulerComponent, schedulerConnection) = await CheckDaprControlPlaneAsync(
            id: "dapr-scheduler",
            name: "Dapr Scheduler",
            connectionLabel: "Scheduled jobs",
            hostAddress: _daprOptions.SchedulerHostAddress,
            cancellationToken);
        components.Add(schedulerComponent);
        connections.Add(schedulerConnection);

        return new SystemHealthReport(
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Components: components,
            Connections: connections);
    }

    private async Task<(ComponentHealth, ConnectionHealth)> CheckPostgresAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            // Simple round-trip query — confirms both reachability and that
            // the connection pool / auth are working.
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            if (!canConnect)
            {
                var down = new ComponentHealth(
                    "postgres", "PostgreSQL", "database",
                    HealthStatus.Down, "CanConnectAsync returned false", null, latency);
                return (down, new ConnectionHealth(
                    "autonate-web", "postgres", "EF Core / Npgsql",
                    HealthStatus.Down, "Database refused connection", latency));
            }

            var component = new ComponentHealth(
                "postgres", "PostgreSQL", "database",
                HealthStatus.Up, "Reachable", null, latency);
            var connection = new ConnectionHealth(
                "autonate-web", "postgres", "EF Core / Npgsql",
                HealthStatus.Up, null, latency);
            return (component, connection);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Postgres health probe failed.");
            var component = new ComponentHealth(
                "postgres", "PostgreSQL", "database",
                HealthStatus.Down, ex.Message, null, (int)stopwatch.ElapsedMilliseconds);
            var connection = new ConnectionHealth(
                "autonate-web", "postgres", "EF Core / Npgsql",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, connection);
        }
    }

    private async Task<(ComponentHealth, ConnectionHealth)> CheckDaprHttpAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var available = await daprSidecarProbe.IsAvailableAsync(cancellationToken);
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            var details = new Dictionary<string, string>
            {
                ["httpEndpoint"] = _daprOptions.HttpEndpoint,
                ["grpcEndpoint"] = _daprOptions.GrpcEndpoint,
                ["appId"] = _daprOptions.AppId
            };

            var component = new ComponentHealth(
                "dapr-sidecar", "Dapr Sidecar", "sidecar",
                available ? HealthStatus.Up : HealthStatus.Down,
                available ? "Healthy" : "Sidecar /v1.0/metadata not responding",
                details,
                latency);
            var connection = new ConnectionHealth(
                "autonate-web", "dapr-sidecar", "HTTP",
                available ? HealthStatus.Up : HealthStatus.Down,
                available ? null : "Cannot reach sidecar — see startup error.",
                latency);
            return (component, connection);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Dapr sidecar HTTP probe failed.");
            var component = new ComponentHealth(
                "dapr-sidecar", "Dapr Sidecar", "sidecar",
                HealthStatus.Down, ex.Message, null, (int)stopwatch.ElapsedMilliseconds);
            var connection = new ConnectionHealth(
                "autonate-web", "dapr-sidecar", "HTTP",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, connection);
        }
    }

    // Probe the pub/sub component by publishing a tiny envelope to a
    // dedicated health-check topic. A healthy sidecar with a live NATS
    // connection returns 204; when the sidecar's NATS connection has died,
    // Dapr returns a 500 with "nats: connection closed" in the body — the
    // exact failure mode that strands subscriptions silently because the SDK
    // SubscribeAsync call still appears to succeed.
    private async Task<(ComponentHealth, ConnectionHealth)> CheckDaprPubSubAsync(
        HealthStatus daprHttpStatus,
        CancellationToken cancellationToken)
    {
        if (daprHttpStatus != HealthStatus.Up)
        {
            var unknownComponent = new ComponentHealth(
                "dapr-pubsub", "Dapr Pub/Sub Component", "component",
                HealthStatus.Unknown, "Sidecar HTTP unreachable; cannot probe.",
                new Dictionary<string, string> { ["pubsubName"] = _daprOptions.PubSubName },
                null);
            var unknownConnection = new ConnectionHealth(
                "autonate-web", "dapr-pubsub", "Pub/Sub publish",
                HealthStatus.Unknown, "Sidecar HTTP unreachable", null);
            return (unknownComponent, unknownConnection);
        }

        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint)
            || string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            var misconfiguredComponent = new ComponentHealth(
                "dapr-pubsub", "Dapr Pub/Sub Component", "component",
                HealthStatus.Unknown, "Dapr endpoint or PubSubName not configured",
                null, null);
            var misconfiguredConnection = new ConnectionHealth(
                "autonate-web", "dapr-pubsub", "Pub/Sub publish",
                HealthStatus.Unknown, "Configuration missing", null);
            return (misconfiguredComponent, misconfiguredConnection);
        }

        var pubsub = Uri.EscapeDataString(_daprOptions.PubSubName);
        // Use a dedicated probe topic that maps to one of the existing
        // streams (the autonate `application.*` subjects) so JetStream has a
        // home for it. Anything outside the provisioned subjects would be
        // rejected even when the sidecar is healthy.
        var probeTopic = $"{DaprApplicationEventPublisher.TopicRoot}.healthprobe";
        var publishUri = new Uri(endpoint, $"/v1.0/publish/{pubsub}/{Uri.EscapeDataString(probeTopic)}?metadata.rawPayload=true");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = new ByteArrayContent("{}"u8.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);
            using var response = await httpClient.PostAsync(publishUri, content, cancellationToken);
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                var detailsOk = new Dictionary<string, string>
                {
                    ["pubsubName"] = _daprOptions.PubSubName,
                    ["probeTopic"] = probeTopic,
                    ["statusCode"] = ((int)response.StatusCode).ToString()
                };
                var component = new ComponentHealth(
                    "dapr-pubsub", "Dapr Pub/Sub Component", "component",
                    HealthStatus.Up, "Publish probe succeeded",
                    detailsOk, latency);
                var connection = new ConnectionHealth(
                    "autonate-web", "dapr-pubsub", "Pub/Sub publish",
                    HealthStatus.Up, null, latency);
                return (component, connection);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var summary = SummarizeDaprError(body, response.StatusCode);
            // Surface the underlying NATS error in the message so the SPA
            // can render it inline. This is exactly the case where Dapr
            // accepted the request but the JetStream subject was never
            // covered or the NATS socket is closed.
            var detailsErr = new Dictionary<string, string>
            {
                ["pubsubName"] = _daprOptions.PubSubName,
                ["probeTopic"] = probeTopic,
                ["statusCode"] = ((int)response.StatusCode).ToString(),
                ["body"] = body.Length > 500 ? body[..500] : body
            };
            var down = new ComponentHealth(
                "dapr-pubsub", "Dapr Pub/Sub Component", "component",
                HealthStatus.Down, summary, detailsErr, latency);
            var downConnection = new ConnectionHealth(
                "autonate-web", "dapr-pubsub", "Pub/Sub publish",
                HealthStatus.Down, summary, latency);
            return (down, downConnection);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Dapr pubsub health probe failed.");
            var component = new ComponentHealth(
                "dapr-pubsub", "Dapr Pub/Sub Component", "component",
                HealthStatus.Down, ex.Message, null, (int)stopwatch.ElapsedMilliseconds);
            var connection = new ConnectionHealth(
                "autonate-web", "dapr-pubsub", "Pub/Sub publish",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, connection);
        }
    }

    private async Task<(ComponentHealth, ConnectionHealth)> CheckNatsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_natsOptions.Url))
        {
            var unknown = new ComponentHealth(
                "nats", "NATS / JetStream", "broker",
                HealthStatus.Unknown, "Nats:Url not configured", null, null);
            var unknownConn = new ConnectionHealth(
                "autonate-web", "nats", "JetStream provisioning",
                HealthStatus.Unknown, "Nats:Url not configured", null);
            return (unknown, unknownConn);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = new NatsConnection(new NatsOpts { Url = _natsOptions.Url });
            await connection.ConnectAsync();
            var js = new NatsJSContext(connection);

            // Inspect the workflow-execution stream for live state. A zero
            // consumer count on a stream that has messages is the smoking
            // gun for the silent-Dapr-NATS-disconnect failure mode.
            var details = new Dictionary<string, string> { ["url"] = _natsOptions.Url };
            try
            {
                var stream = await js.GetStreamAsync("workflow-execution", cancellationToken: cancellationToken);
                details["streamMessages"] = stream.Info.State.Messages.ToString();
                details["streamConsumers"] = stream.Info.State.ConsumerCount.ToString();
                details["streamLastSeq"] = stream.Info.State.LastSeq.ToString();
            }
            catch (NatsJSApiException jsEx) when (jsEx.Error.Code == 404)
            {
                details["stream"] = "workflow-execution stream not found";
            }
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            var component = new ComponentHealth(
                "nats", "NATS / JetStream", "broker",
                HealthStatus.Up, "Reachable", details, latency);
            var conn = new ConnectionHealth(
                "autonate-web", "nats", "JetStream provisioning",
                HealthStatus.Up, null, latency);
            return (component, conn);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "NATS health probe failed.");
            var component = new ComponentHealth(
                "nats", "NATS / JetStream", "broker",
                HealthStatus.Down, ex.Message,
                new Dictionary<string, string> { ["url"] = _natsOptions.Url },
                (int)stopwatch.ElapsedMilliseconds);
            var conn = new ConnectionHealth(
                "autonate-web", "nats", "JetStream provisioning",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, conn);
        }
    }

    // Dapr → NATS is the most useful edge to surface since that's where
    // silent disconnects strand the app. We infer it from the publish probe:
    // if pub/sub is up, Dapr definitely has a working NATS connection; if it
    // returned "connection closed", we mark this edge down with that reason.
    private static ConnectionHealth BuildPubSubToNatsEdge(
        ComponentHealth pubsub,
        ComponentHealth nats)
    {
        if (pubsub.Status == HealthStatus.Up && nats.Status == HealthStatus.Up)
        {
            return new ConnectionHealth(
                "dapr-pubsub", "nats", "JetStream pub/sub",
                HealthStatus.Up, null, null);
        }

        if (pubsub.Status == HealthStatus.Down)
        {
            return new ConnectionHealth(
                "dapr-pubsub", "nats", "JetStream pub/sub",
                HealthStatus.Down,
                pubsub.Message ?? "Sidecar pub/sub component cannot publish",
                null);
        }

        return new ConnectionHealth(
            "dapr-pubsub", "nats", "JetStream pub/sub",
            HealthStatus.Unknown, "Could not infer pub/sub→NATS state", null);
    }

    // Probe Redis through Dapr's state API. We don't talk to Redis directly
    // — Dapr is the only consumer here — so a GET on the configured state
    // store is the most faithful check. A 204 means Dapr reached Redis and
    // the key just doesn't exist (the healthy path); any non-2xx surfaces
    // the underlying Redis error in the body the same way the pub/sub probe
    // surfaces "nats: connection closed".
    private async Task<(ComponentHealth Component, ConnectionHealth AppToState, ConnectionHealth StateToRedis)> CheckRedisStateAsync(
        HealthStatus daprHttpStatus,
        CancellationToken cancellationToken)
    {
        var storeName = string.IsNullOrWhiteSpace(_daprOptions.StateStoreName) ? "statestore" : _daprOptions.StateStoreName;

        if (daprHttpStatus != HealthStatus.Up
            || !Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint))
        {
            var unknown = new ComponentHealth(
                "redis", "Redis (Dapr state store)", "cache",
                HealthStatus.Unknown, "Sidecar HTTP unreachable; cannot probe state store.",
                new Dictionary<string, string> { ["stateStoreName"] = storeName }, null);
            var unknownAppToState = new ConnectionHealth(
                "autonate-web", "redis", "Dapr state API",
                HealthStatus.Unknown, "Sidecar HTTP unreachable", null);
            var unknownStateToRedis = new ConnectionHealth(
                "dapr-sidecar", "redis", "Redis state component",
                HealthStatus.Unknown, "Sidecar HTTP unreachable", null);
            return (unknown, unknownAppToState, unknownStateToRedis);
        }

        var stopwatch = Stopwatch.StartNew();
        var probeUri = new Uri(endpoint, $"/v1.0/state/{Uri.EscapeDataString(storeName)}/health-probe");
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);
            using var response = await httpClient.GetAsync(probeUri, cancellationToken);
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            var details = new Dictionary<string, string>
            {
                ["stateStoreName"] = storeName,
                ["statusCode"] = ((int)response.StatusCode).ToString()
            };

            // 204 (key absent) and 200 (key present) both prove the round
            // trip to Redis worked. Anything else is a failure that Dapr
            // surfaces in the body.
            if (response.IsSuccessStatusCode)
            {
                var component = new ComponentHealth(
                    "redis", "Redis (Dapr state store)", "cache",
                    HealthStatus.Up, "State GET succeeded", details, latency);
                var appToState = new ConnectionHealth(
                    "autonate-web", "redis", "Dapr state API",
                    HealthStatus.Up, null, latency);
                var stateToRedis = new ConnectionHealth(
                    "dapr-sidecar", "redis", "Redis state component",
                    HealthStatus.Up, null, latency);
                return (component, appToState, stateToRedis);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var summary = SummarizeDaprError(body, response.StatusCode);
            details["body"] = body.Length > 500 ? body[..500] : body;
            var down = new ComponentHealth(
                "redis", "Redis (Dapr state store)", "cache",
                HealthStatus.Down, summary, details, latency);
            var downAppToState = new ConnectionHealth(
                "autonate-web", "redis", "Dapr state API",
                HealthStatus.Down, summary, latency);
            var downStateToRedis = new ConnectionHealth(
                "dapr-sidecar", "redis", "Redis state component",
                HealthStatus.Down, summary, latency);
            return (down, downAppToState, downStateToRedis);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Redis state health probe failed.");
            var component = new ComponentHealth(
                "redis", "Redis (Dapr state store)", "cache",
                HealthStatus.Down, ex.Message,
                new Dictionary<string, string> { ["stateStoreName"] = storeName },
                (int)stopwatch.ElapsedMilliseconds);
            var appToState = new ConnectionHealth(
                "autonate-web", "redis", "Dapr state API",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            var stateToRedis = new ConnectionHealth(
                "dapr-sidecar", "redis", "Redis state component",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, appToState, stateToRedis);
        }
    }

    private async Task<(ComponentHealth, ConnectionHealth)> CheckFlowableAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_flowableOptions.BaseUrl))
        {
            var unknown = new ComponentHealth(
                "flowable", "Flowable Engine", "service",
                HealthStatus.Unknown, "Flowable:BaseUrl not configured", null, null);
            var unknownConn = new ConnectionHealth(
                "autonate-web", "flowable", "REST API",
                HealthStatus.Unknown, "Flowable:BaseUrl not configured", null);
            return (unknown, unknownConn);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);
            var baseUrl = _flowableOptions.BaseUrl.TrimEnd('/');
            // /service/management/engine returns engine metadata when up;
            // it requires auth, so attach Basic auth from FlowableOptions.
            var probeUrl = $"{baseUrl}/service/management/engine";
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            if (!string.IsNullOrWhiteSpace(_flowableOptions.Username))
            {
                var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                    $"{_flowableOptions.Username}:{_flowableOptions.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            var details = new Dictionary<string, string> { ["baseUrl"] = _flowableOptions.BaseUrl };

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("version", out var version))
                    {
                        details["version"] = version.GetString() ?? "";
                    }
                    if (doc.RootElement.TryGetProperty("name", out var name))
                    {
                        details["engine"] = name.GetString() ?? "";
                    }
                }
                catch (JsonException)
                {
                    // Non-JSON body is fine — we only care that Flowable answered.
                }

                var component = new ComponentHealth(
                    "flowable", "Flowable Engine", "service",
                    HealthStatus.Up, "Engine responding", details, latency);
                var conn = new ConnectionHealth(
                    "autonate-web", "flowable", "REST API",
                    HealthStatus.Up, null, latency);
                return (component, conn);
            }

            var summary = $"HTTP {(int)response.StatusCode}";
            var down = new ComponentHealth(
                "flowable", "Flowable Engine", "service",
                HealthStatus.Down, summary, details, latency);
            var downConn = new ConnectionHealth(
                "autonate-web", "flowable", "REST API",
                HealthStatus.Down, summary, latency);
            return (down, downConn);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Flowable health probe failed.");
            var component = new ComponentHealth(
                "flowable", "Flowable Engine", "service",
                HealthStatus.Down, ex.Message,
                new Dictionary<string, string> { ["baseUrl"] = _flowableOptions.BaseUrl },
                (int)stopwatch.ElapsedMilliseconds);
            var conn = new ConnectionHealth(
                "autonate-web", "flowable", "REST API",
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, conn);
        }
    }

    // Placement and Scheduler are control-plane gRPC services with no
    // health endpoint we can call from outside the daprd process. A TCP
    // connect is the most we can verify from the app side — but it's
    // useful: when one of these is down, the sidecar log fills with
    // `connection refused` and `i/o timeout`, which the user has been
    // seeing on this machine.
    private async Task<(ComponentHealth, ConnectionHealth)> CheckDaprControlPlaneAsync(
        string id,
        string name,
        string connectionLabel,
        string hostAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hostAddress))
        {
            var unknown = new ComponentHealth(
                id, name, "control-plane",
                HealthStatus.Unknown, "Host address not configured", null, null);
            var unknownConn = new ConnectionHealth(
                "dapr-sidecar", id, connectionLabel,
                HealthStatus.Unknown, "Host address not configured", null);
            return (unknown, unknownConn);
        }

        var (host, port, parseError) = ParseHostPort(hostAddress);
        if (parseError is not null)
        {
            var bad = new ComponentHealth(
                id, name, "control-plane",
                HealthStatus.Unknown, parseError,
                new Dictionary<string, string> { ["address"] = hostAddress }, null);
            var badConn = new ConnectionHealth(
                "dapr-sidecar", id, connectionLabel,
                HealthStatus.Unknown, parseError, null);
            return (bad, badConn);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, timeoutCts.Token);
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            var details = new Dictionary<string, string> { ["address"] = $"{host}:{port}" };
            var component = new ComponentHealth(
                id, name, "control-plane",
                HealthStatus.Up, "Reachable", details, latency);
            var connection = new ConnectionHealth(
                "dapr-sidecar", id, connectionLabel,
                HealthStatus.Up, null, latency);
            return (component, connection);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "{Name} health probe failed.", name);
            var component = new ComponentHealth(
                id, name, "control-plane",
                HealthStatus.Down, ex.Message,
                new Dictionary<string, string> { ["address"] = $"{host}:{port}" },
                (int)stopwatch.ElapsedMilliseconds);
            var connection = new ConnectionHealth(
                "dapr-sidecar", id, connectionLabel,
                HealthStatus.Down, ex.Message, (int)stopwatch.ElapsedMilliseconds);
            return (component, connection);
        }
    }

    private static (string Host, int Port, string? Error) ParseHostPort(string hostAddress)
    {
        var trimmed = hostAddress.Trim();
        var colon = trimmed.LastIndexOf(':');
        if (colon <= 0 || colon == trimmed.Length - 1)
        {
            return ("", 0, $"Address '{hostAddress}' is missing host or port");
        }
        var host = trimmed[..colon];
        if (!int.TryParse(trimmed[(colon + 1)..], out var port))
        {
            return ("", 0, $"Address '{hostAddress}' has non-numeric port");
        }
        return (host, port, null);
    }

    private static string SummarizeDaprError(string body, System.Net.HttpStatusCode statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Dapr returned HTTP {(int)statusCode}";
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text!;
                }
            }
        }
        catch (JsonException)
        {
        }
        return $"Dapr returned HTTP {(int)statusCode}";
    }
}
