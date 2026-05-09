using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace AutoNate.Web.Services.Agent.Catalog;

// Push channel for "the chatbot's default model just changed". The Models
// admin page calls BroadcastAsync after every set-default; SPA chatbots
// subscribe over /ws/agent-model-default and update the in-window label
// without a refresh. New connections also receive the current state on
// connect so the label can render immediately.
public sealed class AgentModelDefaultStreamService
{
    public const string WebSocketRoute = "/ws/agent-model-default";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IAgentModelCatalog _catalog;
    private readonly ILogger<AgentModelDefaultStreamService> _logger;
    private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();

    public AgentModelDefaultStreamService(
        IAgentModelCatalog catalog,
        ILogger<AgentModelDefaultStreamService> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        if (_connections.IsEmpty) return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(BuildMessage(), SerializerOptions);
        var dropped = new List<Guid>();
        foreach (var entry in _connections)
        {
            var sent = await entry.Value.TrySendAsync(payload, cancellationToken);
            if (!sent) dropped.Add(entry.Key);
        }
        foreach (var id in dropped)
        {
            if (_connections.TryRemove(id, out var c)) await c.DisposeAsync();
        }
    }

    public async Task AcceptClientAsync(HttpContext context, CancellationToken cancellationToken)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var clientId = Guid.NewGuid();
        var connection = new ClientConnection(socket);
        _connections[clientId] = connection;

        // Send current state immediately so the chatbot label can render
        // before any subsequent broadcasts.
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(BuildMessage(), SerializerOptions);
            await connection.TrySendAsync(payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send initial agent-model-default snapshot to client {ClientId}.", clientId);
        }

        var buffer = new byte[256];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _connections.TryRemove(clientId, out _);
        }
    }

    private AgentModelDefaultMessage BuildMessage()
    {
        var current = _catalog.GetDefault();
        if (current is null) return new AgentModelDefaultMessage(null, null, null);
        return new AgentModelDefaultMessage(current.ModelId, current.DisplayName, current.Provider);
    }

    public sealed record AgentModelDefaultMessage(
        string? ModelId,
        string? DisplayName,
        string? Provider);

    private sealed class ClientConnection(WebSocket socket) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public async Task<bool> TrySendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open) return false;
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (socket.State != WebSocketState.Open) return false;
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (WebSocketException) { return false; }
            finally { _sendLock.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); }
                catch (WebSocketException) { }
            }
            socket.Dispose();
            _sendLock.Dispose();
        }
    }
}
