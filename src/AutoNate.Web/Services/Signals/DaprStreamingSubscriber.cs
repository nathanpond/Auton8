using System.Net.Http.Headers;
using System.Text;
using AutoNate.Web.Configuration;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Workflow;
using Dapr.Messaging.PublishSubscribe;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.Signals;

// Owns every Dapr pub/sub subscription via Dapr.Messaging's streaming
// SubscribeAsync API. Replaces the static `/dapr/subscribe` manifest model:
// topics can be added or removed at runtime without restarting the sidecar.
//
// Two consumer paths:
//   - The telemetry topic (BusWatcherStreamService.TopicName) is always
//     subscribed and pushed into BusWatcherStreamService for WebSocket /
//     in-process fan-out.
//   - Each signal-start-event topic registered by a published workflow is
//     subscribed dynamically; messages are routed to WorkflowSignalDispatcher
//     for matching against configured signal names.
// A single message can hit both paths when a workflow listens for a signal
// on the telemetry topic.
public sealed class DaprStreamingSubscriber(
    DaprPublishSubscribeClient pubSubClient,
    IWorkflowSignalRegistry registry,
    BusWatcherStreamService busWatcher,
    WorkflowSignalDispatcher signalDispatcher,
    IHttpClientFactory httpClientFactory,
    IOptions<DaprOptions> daprOptions,
    ILogger<DaprStreamingSubscriber> logger) : IHostedService, IDaprStreamingSubscriber
{
    // How often the watchdog checks the pub/sub component. 15s is a
    // tradeoff: short enough that recovery feels prompt after Dapr's NATS
    // socket reconnects, long enough that an idle dev box isn't burning
    // cycles on probes.
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(15);

    // How long pub/sub has to stay unhealthy before the watchdog escalates
    // and restarts the sidecar. The Dapr NATS Go client is supposed to
    // reconnect on its own, but in practice it sometimes lands in a
    // "connection closed" state it never crawls out of. Give it a fair
    // window first so we don't punish a transient blip.
    private static readonly TimeSpan UnhealthyEscalationThreshold = TimeSpan.FromSeconds(45);

    // Minimum gap between sidecar restarts. Restarting daprd churns every
    // pub/sub subscription, so we don't want to do it on every tick if
    // NATS itself is genuinely down.
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromSeconds(120);

    private readonly DaprPublishSubscribeClient _pubSubClient = pubSubClient;
    private readonly IWorkflowSignalRegistry _registry = registry;
    private readonly BusWatcherStreamService _busWatcher = busWatcher;
    private readonly WorkflowSignalDispatcher _signalDispatcher = signalDispatcher;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly DaprOptions _daprOptions = daprOptions.Value;
    private readonly ILogger<DaprStreamingSubscriber> _logger = logger;

    private readonly Dictionary<string, IAsyncDisposable> _subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private CancellationTokenSource? _lifetimeCts;
    private Task? _watchdogTask;
    // Tracks the previous probe outcome so we only re-subscribe on the
    // Down→Up edge instead of churning on every successful tick.
    private bool _lastProbeHealthy = true;
    private DateTimeOffset? _firstUnhealthyAt;
    private DateTimeOffset? _lastRestartAt;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts = new CancellationTokenSource();
        await _registry.RefreshAsync(cancellationToken);
        await SyncAsync(cancellationToken);
        _watchdogTask = Task.Run(() => RunWatchdogAsync(_lifetimeCts.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts?.Cancel();

        if (_watchdogTask is not null)
        {
            try
            {
                await _watchdogTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var (topic, handle) in _subscriptions)
            {
                try
                {
                    await handle.DisposeAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to dispose subscription for {Topic}.", topic);
                }
            }
            _subscriptions.Clear();
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // Periodically probe the pub/sub component. When NATS bounces (or any
    // peer like the flowable-dapr sidecar churns enough to drop our
    // sidecar's NATS socket), Dapr returns "nats: connection closed" on
    // every publish/subscribe. Even after Dapr's NATS client reconnects
    // (configured via `maxReconnects: -1` on the component), the JetStream
    // consumers Dapr created for our streaming subscriptions are ephemeral
    // and may not be re-created. Force a resubscribe on the Down→Up edge.
    private async Task RunWatchdogAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WatchdogInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool healthy;
            try
            {
                healthy = await ProbePubSubAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pub/sub watchdog probe threw.");
                healthy = false;
            }

            if (healthy)
            {
                if (!_lastProbeHealthy)
                {
                    _logger.LogInformation(
                        "Dapr pub/sub component recovered; tearing down stale subscriptions and re-syncing.");
                    await ResetSubscriptionsAsync(cancellationToken);
                    try
                    {
                        await SyncAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to re-sync subscriptions after pub/sub recovery.");
                        healthy = false;
                    }
                }

                if (healthy)
                {
                    _firstUnhealthyAt = null;
                }
            }
            else
            {
                _firstUnhealthyAt ??= DateTimeOffset.UtcNow;
                if (_lastProbeHealthy)
                {
                    _logger.LogWarning(
                        "Dapr pub/sub component reports unhealthy; will escalate to a sidecar restart if it stays down for {ThresholdSeconds}s.",
                        (int)UnhealthyEscalationThreshold.TotalSeconds);
                }

                var unhealthyDuration = DateTimeOffset.UtcNow - _firstUnhealthyAt.Value;
                var sinceLastRestart = _lastRestartAt is null
                    ? TimeSpan.MaxValue
                    : DateTimeOffset.UtcNow - _lastRestartAt.Value;

                if (unhealthyDuration >= UnhealthyEscalationThreshold
                    && sinceLastRestart >= RestartCooldown)
                {
                    _lastRestartAt = DateTimeOffset.UtcNow;
                    _logger.LogWarning(
                        "Dapr pub/sub component has been unhealthy for {DurationSeconds}s; restarting the sidecar.",
                        (int)unhealthyDuration.TotalSeconds);
                    var restarted = await TryRestartSidecarAsync(cancellationToken);
                    if (restarted)
                    {
                        // Reset the unhealthy timer so the threshold is
                        // measured again from the restart attempt — the
                        // cooldown still prevents another restart for two
                        // minutes regardless.
                        _firstUnhealthyAt = DateTimeOffset.UtcNow;
                    }
                }
            }

            _lastProbeHealthy = healthy;
        }
    }

    // Shells out to the existing restart-autonate-web-sidecar.sh script.
    // It's the only safe way to recover Dapr from a permanent
    // "nats: connection closed" state without component-yaml changes,
    // because Dapr exposes no API to reload the pub/sub component.
    private async Task<bool> TryRestartSidecarAsync(CancellationToken cancellationToken)
    {
        var scriptPath = ResolveRestartScriptPath();
        if (scriptPath is null)
        {
            _logger.LogError(
                "Cannot restart sidecar: restart-autonate-web-sidecar.sh not found relative to {AppContextBaseDirectory}.",
                AppContext.BaseDirectory);
            return false;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                _logger.LogError("Failed to spawn sidecar restart process for {ScriptPath}.", scriptPath);
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));

            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "Sidecar restart succeeded.\nstdout:\n{Stdout}",
                    stdout.Trim());
                // Re-sync subscriptions immediately so we don't have to
                // wait for the next watchdog tick. The next probe will then
                // confirm pub/sub is healthy and clear _firstUnhealthyAt.
                await ResetSubscriptionsAsync(cancellationToken);
                try
                {
                    await SyncAsync(cancellationToken);
                }
                catch (Exception syncEx)
                {
                    _logger.LogError(syncEx, "Failed to re-sync subscriptions after sidecar restart.");
                }
                return true;
            }

            _logger.LogError(
                "Sidecar restart exited with code {ExitCode}.\nstdout:\n{Stdout}\nstderr:\n{Stderr}",
                process.ExitCode, stdout.Trim(), stderr.Trim());
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sidecar restart threw.");
            return false;
        }
    }

    // The script lives next to the repo, but the running binary is in
    // bin/Debug/net{N}.0. Walk up looking for the infra/ directory so this
    // works in both a Rider run and a published app.
    private static string? ResolveRestartScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "infra", "restart-autonate-web-sidecar.sh");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    // Round-trip publish to a dedicated probe topic. The topic must live
    // under one of the JetStream subjects the stream provisioner covers
    // (here: `application.>`) so a healthy sidecar accepts it cleanly.
    private async Task<bool> ProbePubSubAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_daprOptions.HttpEndpoint, UriKind.Absolute, out var endpoint)
            || string.IsNullOrWhiteSpace(_daprOptions.PubSubName))
        {
            return false;
        }

        var pubsub = Uri.EscapeDataString(_daprOptions.PubSubName);
        var probeTopic = Uri.EscapeDataString($"{DaprApplicationEventPublisher.TopicRoot}.healthprobe");
        var probeUri = new Uri(endpoint, $"/v1.0/publish/{pubsub}/{probeTopic}?metadata.rawPayload=true");

        try
        {
            using var content = new ByteArrayContent("{}"u8.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);
            using var response = await httpClient.PostAsync(probeUri, content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    // Dispose every active subscription handle so SyncAsync rebuilds them
    // from scratch. Used after a Down→Up transition because the SDK's
    // existing handles are pinned to JetStream consumers that no longer
    // exist after the NATS socket bounce.
    private async Task ResetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var (topic, handle) in _subscriptions)
            {
                try
                {
                    await handle.DisposeAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception,
                        "Failed to dispose stale subscription for {Topic} during recovery.", topic);
                }
            }
            _subscriptions.Clear();
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // Reconcile active subscriptions with the registry's current topic set,
    // plus the always-on telemetry topic. Called from StartAsync and after
    // EfCoreWorkflowModelStore.PublishAsync refreshes the registry.
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (_lifetimeCts is null)
        {
            return;
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            // Always-on topics: the workflow telemetry stream, and any topic
            // the app publishes to itself. Subscribing to our own publishes
            // means BusWatcher can show them and signal dispatch works without
            // the operator having to register a workflow first.
            var desired = new HashSet<string>(_registry.GetSubscribedTopics(), StringComparer.Ordinal)
            {
                BusWatcherStreamService.TopicName,
                DaprRecordEventPublisher.TopicName,
                DaprApplicationEventPublisher.TopicName
            };

            foreach (var topic in desired)
            {
                if (_subscriptions.ContainsKey(topic))
                {
                    continue;
                }

                try
                {
                    var handle = await _pubSubClient.SubscribeAsync(
                        _daprOptions.PubSubName,
                        topic,
                        BuildOptions(),
                        (message, ct) => HandleMessageAsync(topic, message, ct),
                        _lifetimeCts.Token);
                    _subscriptions[topic] = handle;
                    _logger.LogInformation("Subscribed to Dapr topic {Topic}.", topic);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to subscribe to Dapr topic {Topic}.", topic);
                }
            }

            var toRemove = _subscriptions.Keys.Where(topic => !desired.Contains(topic)).ToArray();
            foreach (var topic in toRemove)
            {
                if (!_subscriptions.Remove(topic, out var handle))
                {
                    continue;
                }
                try
                {
                    await handle.DisposeAsync();
                    _logger.LogInformation("Unsubscribed from Dapr topic {Topic}.", topic);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to dispose subscription for {Topic}.", topic);
                }
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<TopicResponseAction> HandleMessageAsync(
        string topic,
        TopicMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawPayload = message.Data.IsEmpty
                ? string.Empty
                : Encoding.UTF8.GetString(message.Data.Span);

            // Pretty-print every JSON payload on the way to BusWatcher — the
            // SPA's live page is meant to be human-readable. Signals still go
            // to Flowable as raw eventData, so the dispatcher reads from the
            // un-prettified original below.
            var displayPayload = BusWatcherStreamService.FormatJson(rawPayload);

            var busMessage = new BusWatcherStreamService.BusWatcherMessage(
                DateTimeOffset.UtcNow,
                topic,
                string.IsNullOrWhiteSpace(message.DataContentType) ? "application/json" : message.DataContentType,
                BuildHeaders(message),
                displayPayload);

            // BusWatcher is a general live feed of every subscribed topic, not
            // just the workflow telemetry one — operators rely on it to see
            // what's flowing through pub/sub.
            await _busWatcher.PublishAsync(busMessage, cancellationToken);

            if (_registry.GetSignalNamesForTopic(topic).Count > 0)
            {
                // Signal dispatch reads `eventType` out of the original
                // payload; pass the raw bytes (not the prettified copy used
                // for display) so JsonDocument.Parse sees the same shape the
                // publisher produced.
                var signalMessage = busMessage with { Payload = rawPayload };
                await _signalDispatcher.HandleAsync(signalMessage);
            }

            return TopicResponseAction.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed handling Dapr message on topic {Topic}.", topic);
            // Drop rather than retry: Flowable signal delivery is best-effort
            // here and re-running the same payload won't make a malformed one
            // succeed. Durability for downstream Flowable failures is a
            // separate concern (see the durability roadmap).
            return TopicResponseAction.Drop;
        }
    }

    private static DaprSubscriptionOptions BuildOptions()
    {
        // rawPayload=true matches the publisher side: the Flowable extension
        // posts bytes with `?metadata.rawPayload=true`, and any external
        // signal producer is expected to do the same. Without this, Dapr
        // tries to parse incoming bytes as a CloudEvents envelope and the
        // handler never sees the message.
        return new DaprSubscriptionOptions(
            new MessageHandlingPolicy(TimeSpan.FromSeconds(30), TopicResponseAction.Retry))
        {
            Metadata = new Dictionary<string, string>
            {
                ["rawPayload"] = "true"
            }
        };
    }

    private static Dictionary<string, string> BuildHeaders(TopicMessage message)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddIfPresent(headers, "ce-id", message.Id);
        AddIfPresent(headers, "ce-source", message.Source);
        AddIfPresent(headers, "ce-type", message.Type);
        AddIfPresent(headers, "ce-specversion", message.SpecVersion);
        AddIfPresent(headers, "ce-topic", message.Topic);
        AddIfPresent(headers, "ce-pubsubname", message.PubSubName);
        AddIfPresent(headers, "content-type", message.DataContentType);

        return headers;
    }

    private static void AddIfPresent(Dictionary<string, string> headers, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            headers[key] = value;
        }
    }
}

// Exposed as an interface so EfCoreWorkflowModelStore (which lives in the
// Workflow layer) can request a re-sync after publishing without taking a
// hard dependency on the concrete subscriber type.
public interface IDaprStreamingSubscriber
{
    Task SyncAsync(CancellationToken cancellationToken);
}
