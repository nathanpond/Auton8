using System.Text;
using AutoNate.Web.Configuration;
using AutoNate.Web.Services.BusWatcher;
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
    IOptions<DaprOptions> daprOptions,
    ILogger<DaprStreamingSubscriber> logger) : IHostedService, IDaprStreamingSubscriber
{
    private readonly DaprPublishSubscribeClient _pubSubClient = pubSubClient;
    private readonly IWorkflowSignalRegistry _registry = registry;
    private readonly BusWatcherStreamService _busWatcher = busWatcher;
    private readonly WorkflowSignalDispatcher _signalDispatcher = signalDispatcher;
    private readonly DaprOptions _daprOptions = daprOptions.Value;
    private readonly ILogger<DaprStreamingSubscriber> _logger = logger;

    private readonly Dictionary<string, IAsyncDisposable> _subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private CancellationTokenSource? _lifetimeCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts = new CancellationTokenSource();
        await _registry.RefreshAsync(cancellationToken);
        await SyncAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts?.Cancel();

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
            var desired = new HashSet<string>(_registry.GetSubscribedTopics(), StringComparer.Ordinal)
            {
                BusWatcherStreamService.TopicName
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

            var isTelemetry = string.Equals(topic, BusWatcherStreamService.TopicName, StringComparison.Ordinal);
            // Telemetry feeds the BusWatcher live page in the SPA — pretty-print
            // there. Signals go straight to Flowable as eventData; compact bytes
            // are fine and slightly cheaper to store.
            var displayPayload = isTelemetry
                ? BusWatcherStreamService.FormatJson(rawPayload)
                : rawPayload;

            var busMessage = new BusWatcherStreamService.BusWatcherMessage(
                DateTimeOffset.UtcNow,
                topic,
                string.IsNullOrWhiteSpace(message.DataContentType) ? "application/json" : message.DataContentType,
                BuildHeaders(message),
                displayPayload);

            if (isTelemetry)
            {
                await _busWatcher.PublishAsync(busMessage, cancellationToken);
            }

            // A workflow may still register a signal on the telemetry topic
            // (we lifted that ban deliberately), so always also try dispatch.
            if (_registry.GetSignalNamesForTopic(topic).Count > 0)
            {
                await _signalDispatcher.HandleAsync(busMessage);
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
