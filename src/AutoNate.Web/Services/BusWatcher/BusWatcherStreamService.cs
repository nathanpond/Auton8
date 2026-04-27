using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace AutoNate.Web.Services.BusWatcher;

// Owns the in-process and WebSocket fan-out for the workflow telemetry stream.
//
// Inbound messages arrive via the Dapr streaming subscriber (Services/Signals/
// DaprStreamingSubscriber.cs), which calls PublishAsync to feed them in. From
// there they reach two consumer types:
//   - In-process subscribers via Subscribe(...) — used today only for tests
//     and any future server-side consumers of the telemetry stream.
//   - WebSocket clients connected at /ws/bus-watcher (the SPA's BusWatcher
//     live page).
public sealed class BusWatcherStreamService(ILogger<BusWatcherStreamService> logger)
{
    public const string WebSocketRoute = "/ws/bus-watcher";
    public const string TopicRoot = "workflow.execution";
    public const string TopicName = "workflow.execution.events";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ILogger<BusWatcherStreamService> _logger = logger;
    private readonly ConcurrentDictionary<Guid, BusWatcherClientConnection> _connections = new();
    private readonly ConcurrentDictionary<Guid, Func<BusWatcherMessage, Task>> _messageSubscribers = new();

    public async Task PublishAsync(BusWatcherMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "BusWatcher publishing message for topic {Topic} to {SubscriberCount} in-process subscribers and {WebSocketCount} websocket clients.",
            message.Topic,
            _messageSubscribers.Count,
            _connections.Count);

        await NotifySubscribersAsync(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        var disconnectedClientIds = new List<Guid>();

        foreach (var entry in _connections)
        {
            var sent = await entry.Value.TrySendAsync(payload, cancellationToken);
            if (!sent)
            {
                disconnectedClientIds.Add(entry.Key);
            }
        }

        foreach (var disconnectedClientId in disconnectedClientIds)
        {
            if (_connections.TryRemove(disconnectedClientId, out var connection))
            {
                await connection.DisposeAsync();
            }
        }
    }

    public IDisposable Subscribe(Func<BusWatcherMessage, Task> handler)
    {
        var subscriptionId = Guid.NewGuid();
        _messageSubscribers[subscriptionId] = handler;
        _logger.LogInformation("BusWatcher registered in-process subscriber {SubscriptionId}. Total subscribers: {SubscriberCount}.", subscriptionId, _messageSubscribers.Count);
        return new BusWatcherSubscription(_messageSubscribers, subscriptionId, _logger);
    }

    private async Task NotifySubscribersAsync(BusWatcherMessage message)
    {
        foreach (var subscriber in _messageSubscribers.Values)
        {
            try
            {
                await subscriber(message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "BusWatcher in-process subscriber threw while handling topic {Topic}.", message.Topic);
            }
        }
    }

    public async Task AcceptClientAsync(HttpContext context, CancellationToken cancellationToken)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var clientId = Guid.NewGuid();
        var connection = new BusWatcherClientConnection(socket);
        _connections[clientId] = connection;

        var buffer = new byte[1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _connections.TryRemove(clientId, out _);
        }
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

    private sealed class BusWatcherClientConnection(WebSocket socket) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public async Task<bool> TrySendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open)
            {
                return false;
            }

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (socket.State != WebSocketState.Open)
                {
                    return false;
                }

                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebSocketException)
            {
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
            }

            socket.Dispose();
            _sendLock.Dispose();
        }
    }

    private sealed class BusWatcherSubscription(
        ConcurrentDictionary<Guid, Func<BusWatcherMessage, Task>> subscribers,
        Guid subscriptionId,
        ILogger<BusWatcherStreamService> logger) : IDisposable
    {
        public void Dispose()
        {
            subscribers.TryRemove(subscriptionId, out _);
            logger.LogInformation("BusWatcher removed in-process subscriber {SubscriptionId}. Remaining subscribers: {SubscriberCount}.", subscriptionId, subscribers.Count);
        }
    }
}
