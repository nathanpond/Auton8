using System.Net.WebSockets;
using System.Security.Claims;
using System.Threading.Channels;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Per-websocket state. Owns the socket, a bounded outbound frame queue, and
// the shutdown signal used to stop the read/write loops in concert. The
// outbound queue is bounded so a slow consumer can be detected (TryEnqueue
// returns false → manager closes the connection) instead of backing up the
// fan-out path for everyone.
public sealed class SubscriptionConnection : IAsyncDisposable
{
    public const int DefaultOutboundCapacity = 512;

    private readonly WebSocket _socket;
    private readonly Channel<byte[]> _outbound;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    public SubscriptionConnection(
        Guid connectionId,
        WebSocket socket,
        ClaimsPrincipal principal,
        ActorAuthSnapshot snapshot,
        int outboundCapacity = DefaultOutboundCapacity)
    {
        ConnectionId = connectionId;
        _socket = socket;
        Principal = principal;
        Snapshot = snapshot;
        _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(outboundCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public Guid ConnectionId { get; }
    public ClaimsPrincipal Principal { get; }
    public ActorAuthSnapshot Snapshot { get; private set; }
    public SubscriptionAuthGate AuthGate { get; } = new();
    public CancellationToken Shutdown => _shutdown.Token;
    public WebSocketState SocketState => _socket.State;

    // Replace the snapshot when AuthChangeListener detects an actor-grant or
    // membership change. Pairs with `AuthGate.Clear()` because cached
    // decisions were made against the old snapshot's grant set.
    public void ReplaceSnapshot(ActorAuthSnapshot snapshot)
    {
        Snapshot = snapshot;
        AuthGate.Clear();
    }

    public bool TryEnqueue(byte[] frame) => _outbound.Writer.TryWrite(frame);

    public void RequestShutdown()
    {
        if (!_shutdown.IsCancellationRequested)
        {
            try { _shutdown.Cancel(); } catch (ObjectDisposedException) { /* already disposed by a concurrent teardown */ }
        }
        _outbound.Writer.TryComplete();
    }

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);

    public async Task WriteLoopAsync(CancellationToken externalCancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation, _shutdown.Token);
        try
        {
            await foreach (var frame in _outbound.Reader.ReadAllAsync(linked.Token))
            {
                if (_socket.State != WebSocketState.Open) return;
                await _sendLock.WaitAsync(linked.Token);
                try
                {
                    if (_socket.State != WebSocketState.Open) return;
                    await _socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, linked.Token);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or client disconnect; finally does the teardown.
        }
        catch (WebSocketException)
        {
            // Peer vanished mid-frame; same teardown path.
        }
        finally
        {
            RequestShutdown();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RequestShutdown();

        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Socket already gone — nothing left to send on.
            }
            catch (OperationCanceledException)
            {
                // Shutdown raced the send; dropping the frame is correct.
            }
        }

        _socket.Dispose();
        _sendLock.Dispose();
        _shutdown.Dispose();
    }
}
