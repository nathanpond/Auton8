using System.Collections.Concurrent;
using System.Text.Json;

namespace AutoNate.Web.Services.BusWatcher;

// In-process pub/sub bridge for the workflow telemetry stream. Inbound
// messages arrive via DaprStreamingSubscriber (Services/Signals/) which calls
// PublishAsync. From there each message is delivered to every in-process
// subscriber registered via Subscribe(...).
//
// Live websocket fan-out for the SPA's BusWatcher page is now handled by
// SubscriptionManager (Services/BusWatcher/Subscriptions/), which itself
// subscribes via Subscribe(...). AuthChangeListener also rides this path to
// react to iam.events mutations. Phase 3 removed the standalone WS broadcast
// loop that used to live here.
public sealed class BusWatcherStreamService(ILogger<BusWatcherStreamService> logger)
{
    public const string WebSocketRoute = "/ws/bus-watcher";
    public const string TopicRoot = "workflow.execution";
    public const string TopicName = "workflow.execution.events";

    private readonly ILogger<BusWatcherStreamService> _logger = logger;
    // The token is part of the delegate so PublishAsync can forward it. Before
    // #76 the signature had nowhere to put it, so a subscriber could not be
    // cancelled even though Dapr's MessageHandlingPolicy gives the whole
    // dispatch a 30 s budget and redelivers past it.
    private readonly ConcurrentDictionary<Guid, Func<BusWatcherMessage, CancellationToken, Task>> _messageSubscribers = new();

    public async Task PublishAsync(BusWatcherMessage message, CancellationToken cancellationToken)
    {
        // Debug, not Information: this is per-message on a hot path (#76).
        _logger.LogDebug(
            "BusWatcher publishing message for topic {Topic} to {SubscriberCount} in-process subscribers.",
            message.Topic,
            _messageSubscribers.Count);

        // Still serial. Subscribers were written against serial delivery, so
        // making the fan-out concurrent is a behaviour change rather than a
        // fix; forwarding the token at least lets a slow one be cut off.
        foreach (var subscriber in _messageSubscribers.Values)
        {
            try
            {
                await subscriber(message, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "BusWatcher in-process subscriber threw while handling topic {Topic}.", message.Topic);
            }
        }
    }

    public IDisposable Subscribe(Func<BusWatcherMessage, CancellationToken, Task> handler)
    {
        var subscriptionId = Guid.NewGuid();
        _messageSubscribers[subscriptionId] = handler;
        _logger.LogInformation(
            "BusWatcher registered in-process subscriber {SubscriptionId}. Total subscribers: {SubscriberCount}.",
            subscriptionId, _messageSubscribers.Count);
        return new BusWatcherSubscription(_messageSubscribers, subscriptionId, _logger);
    }

    // Pretty-formats a JSON payload for display. The streaming subscriber
    // pre-formats the telemetry stream for the BusWatcher live page; non-JSON
    // payloads round-trip unchanged.
    public static string FormatJson(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    public sealed record BusWatcherMessage(
        DateTimeOffset ReceivedAtUtc,
        string Topic,
        string? ContentType,
        Dictionary<string, string> Headers,
        string Payload);

    private sealed class BusWatcherSubscription(
        ConcurrentDictionary<Guid, Func<BusWatcherMessage, CancellationToken, Task>> subscribers,
        Guid subscriptionId,
        ILogger<BusWatcherStreamService> logger) : IDisposable
    {
        public void Dispose()
        {
            subscribers.TryRemove(subscriptionId, out _);
            logger.LogInformation(
                "BusWatcher removed in-process subscriber {SubscriptionId}. Remaining subscribers: {SubscriberCount}.",
                subscriptionId, subscribers.Count);
        }
    }
}
