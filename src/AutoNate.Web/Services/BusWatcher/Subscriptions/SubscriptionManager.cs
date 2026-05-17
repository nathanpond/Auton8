using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Singleton. Replaces the firehose broadcast loop in BusWatcherStreamService.
// Owns per-connection state, handles the subscribe/unsubscribe/ping wire
// protocol, and fans inbound bus messages out to subscribed connections via
// the resolver registry. Per-message authorization is enforced through each
// delivery's FastGate or (future) GateTarget; subscribe-time authorization
// flows through ChannelSubscribeGateRegistry.
public sealed class SubscriptionManager
{
    private readonly SubscriptionRegistry _registry;
    private readonly ChannelResolverRegistry _resolvers;
    private readonly ChannelSubscribeGateRegistry _gates;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionManager> _logger;
    private readonly ConcurrentDictionary<Guid, SubscriptionConnection> _connections = new();

    public SubscriptionManager(
        SubscriptionRegistry registry,
        ChannelResolverRegistry resolvers,
        ChannelSubscribeGateRegistry gates,
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionManager> logger)
    {
        _registry = registry;
        _resolvers = resolvers;
        _gates = gates;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public int ConnectionCount => _connections.Count;

    // Called by AuthChangeListener when permission grants or memberships
    // change. Rebuilds every connected actor's ActorAuthSnapshot, drops their
    // AuthGate cache, and sends an `invalidate` frame so the SPA refetches
    // affected react-query keys. Connections whose principal no longer
    // resolves to a valid actor (e.g. user deleted) get shut down.
    public async Task BroadcastAuthInvalidationAsync(string reason, CancellationToken cancellationToken)
    {
        var connections = _connections.Values.ToArray();
        foreach (var connection in connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = _scopeFactory.CreateAsyncScope();
            var authorizer = scope.ServiceProvider.GetRequiredService<IAuthorizer>();
            var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthorizationOptions>>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

            var refreshed = await ActorAuthSnapshot.LoadAsync(
                connection.Principal, authorizer, authOptions, dbFactory, cancellationToken);
            if (refreshed is null)
            {
                _logger.LogInformation(
                    "Connection {ConnectionId}'s actor no longer resolves; shutting down.",
                    connection.ConnectionId);
                connection.RequestShutdown();
                continue;
            }

            connection.ReplaceSnapshot(refreshed);

            var subscribedChannels = _registry.GetChannelsForConnection(connection.ConnectionId);
            if (subscribedChannels.Count == 0) continue;
            var frame = SubscriptionProtocol.Serialize(ServerInvalidateFrame.For(subscribedChannels, reason));
            if (!connection.TryEnqueue(frame))
            {
                _logger.LogWarning(
                    "Subscription connection {ConnectionId} outbound queue full on invalidate; requesting shutdown.",
                    connection.ConnectionId);
                connection.RequestShutdown();
            }
        }
    }

    public async Task AcceptAsync(
        HttpContext context,
        IAuthorizer authorizer,
        IOptions<AuthorizationOptions> authorizationOptions,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var snapshot = await ActorAuthSnapshot.LoadAsync(context.User, authorizer, authorizationOptions, dbFactory, cancellationToken);
        if (snapshot is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new SubscriptionConnection(Guid.NewGuid(), socket, context.User, snapshot);
        _connections[connection.ConnectionId] = connection;

        try
        {
            var write = connection.WriteLoopAsync(cancellationToken);
            var read = ReadLoopAsync(connection, cancellationToken);
            await Task.WhenAll(write, read);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription connection {ConnectionId} terminated abnormally.", connection.ConnectionId);
        }
        finally
        {
            _connections.TryRemove(connection.ConnectionId, out _);
            _registry.RemoveConnection(connection.ConnectionId);
            await connection.DisposeAsync();
        }
    }

    // Called by DaprStreamingSubscriber on every inbound bus message (when the
    // scoped-subscriptions feature flag is on). Resolves the message to its
    // delivery channels, then fans out to each channel's subscribed
    // connections, applying per-recipient gates.
    public async Task PublishAsync(BusWatcherStreamService.BusWatcherMessage message, CancellationToken cancellationToken)
    {
        var deliveries = _resolvers.Resolve(message);
        if (deliveries.Count == 0)
        {
            return;
        }

        foreach (var delivery in deliveries)
        {
            var subscribers = _registry.SnapshotSubscribers(delivery.ChannelName);
            if (subscribers.Count == 0)
            {
                continue;
            }

            byte[]? frame = null;
            foreach (var subscriber in subscribers)
            {
                if (delivery.FastGate is not null && !delivery.FastGate(subscriber.Snapshot))
                {
                    continue;
                }
                if (delivery.GateTarget is { } target)
                {
                    var allowed = await CheckAuthorizedAsync(subscriber, target, cancellationToken);
                    if (!allowed)
                    {
                        continue;
                    }
                }
                frame ??= SubscriptionProtocol.Serialize(ServerEventFrame.From(delivery.ChannelName, message));
                if (!subscriber.TryEnqueue(frame))
                {
                    _logger.LogWarning(
                        "Subscription connection {ConnectionId} outbound queue full; requesting shutdown.",
                        subscriber.ConnectionId);
                    subscriber.RequestShutdown();
                }
            }
        }
    }

    // Per-recipient IAuthorizer gate for entity-instance deliveries. SuperAdmin
    // short-circuits without a DB hit; otherwise the per-connection LRU
    // amortizes stable subscriptions to one DB lookup per (entity, action) per
    // ~30s. Cache misses spin up a transient request scope to resolve a fresh
    // IAuthorizer.
    internal async Task<bool> CheckAuthorizedAsync(
        SubscriptionConnection subscriber,
        EntityRef target,
        CancellationToken cancellationToken)
    {
        if (subscriber.Snapshot.IsSuperAdmin)
        {
            return true;
        }

        var key = new SubscriptionAuthGate.CacheKey(target.Kind, target.Id, Actions.View);
        if (subscriber.AuthGate.TryGet(key, out var cached))
        {
            return cached;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var decision = target.Kind switch
        {
            EntityKinds.Page when Guid.TryParse(target.Id, out var pageId) =>
                await scope.ServiceProvider.GetRequiredService<IContentAuthorizer>()
                    .AuthorizeAsync(subscriber.Principal, EntityKinds.Page, pageId, Actions.View, cancellationToken),
            _ =>
                await scope.ServiceProvider.GetRequiredService<IAuthorizer>()
                    .AuthorizeAsync(subscriber.Principal, Actions.View, target, cancellationToken),
        };
        var allowed = decision.IsAllowed;
        subscriber.AuthGate.Set(key, allowed);
        return allowed;
    }

    // Per-frame idle timeout. A client that doesn't send anything (including a
    // ping) within this window gets disconnected so the server doesn't hold a
    // snapshot + auth cache for a connection whose TCP leg silently died.
    // Clients are expected to ping every 25s; 90s leaves room for two missed
    // pings before we drop them.
    internal static readonly TimeSpan IdleFrameTimeout = TimeSpan.FromSeconds(90);

    private async Task ReadLoopAsync(SubscriptionConnection connection, CancellationToken externalCancellation)
    {
        var buffer = new byte[4096];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation, connection.Shutdown);

        try
        {
            while (connection.SocketState == WebSocketState.Open)
            {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                idleCts.CancelAfter(IdleFrameTimeout);

                using var accumulator = new MemoryStream();
                ValueWebSocketReceiveResult result;
                try
                {
                    do
                    {
                        result = await connection.ReceiveAsync(buffer, idleCts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }
                        await accumulator.WriteAsync(buffer.AsMemory(0, result.Count), idleCts.Token);
                    } while (!result.EndOfMessage);
                }
                catch (OperationCanceledException ex) when (idleCts.IsCancellationRequested && !linked.IsCancellationRequested)
                {
                    // Idle timeout — drop the connection rather than continue.
                    _logger.LogInformation(ex,
                        "Connection {ConnectionId} idle-closed after {Seconds}s without a frame.",
                        connection.ConnectionId, IdleFrameTimeout.TotalSeconds);
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var frameBytes = accumulator.ToArray();
                await HandleClientFrameAsync(connection, frameBytes, linked.Token);
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
            connection.RequestShutdown();
        }
    }

    private async Task HandleClientFrameAsync(SubscriptionConnection connection, byte[] frameBytes, CancellationToken cancellationToken)
    {
        var frame = SubscriptionProtocol.TryParseClientFrame(frameBytes);
        if (frame is null || string.IsNullOrEmpty(frame.Type))
        {
            EnqueueOrShutdown(connection, ServerErrorFrame.For("protocol", "malformed frame"));
            return;
        }

        switch (frame.Type)
        {
            case SubscriptionFrameType.Subscribe:
                await HandleSubscribeAsync(connection, frame, cancellationToken);
                break;
            case SubscriptionFrameType.Unsubscribe:
                HandleUnsubscribe(connection, frame);
                break;
            case SubscriptionFrameType.Ping:
                HandlePing(connection, frame);
                break;
            default:
                EnqueueOrShutdown(connection, ServerErrorFrame.For("protocol", $"unknown frame type '{frame.Type}'"));
                break;
        }
    }

    private async Task HandleSubscribeAsync(SubscriptionConnection connection, ClientFrame frame, CancellationToken cancellationToken)
    {
        var requested = frame.Channels ?? Array.Empty<string>();
        var subscribed = new List<string>(requested.Count);
        var rejected = new List<RejectedChannel>();

        foreach (var raw in requested)
        {
            if (!ChannelName.TryParse(raw, out var parsed) || parsed is null)
            {
                rejected.Add(new RejectedChannel(raw, SubscriptionRejectCode.Malformed, "channel name is malformed"));
                continue;
            }

            var gate = _gates.Find(parsed.Kind);
            if (gate is null)
            {
                rejected.Add(new RejectedChannel(parsed.Full, SubscriptionRejectCode.UnknownChannel, $"no gate registered for kind '{parsed.Kind}'"));
                continue;
            }

            var result = await gate.AuthorizeAsync(parsed, connection, cancellationToken);
            if (!result.Allowed)
            {
                rejected.Add(new RejectedChannel(parsed.Full, result.RejectCode ?? SubscriptionRejectCode.Forbidden, result.RejectReason));
                continue;
            }

            _registry.Subscribe(parsed.Full, connection);
            subscribed.Add(parsed.Full);
        }

        var ack = ServerAckFrame.ForSubscribe(
            frame.Id ?? string.Empty,
            subscribed.Count == 0 ? null : subscribed,
            rejected.Count == 0 ? null : rejected);
        EnqueueOrShutdown(connection, ack);
    }

    private void HandleUnsubscribe(SubscriptionConnection connection, ClientFrame frame)
    {
        var requested = frame.Channels ?? Array.Empty<string>();
        foreach (var raw in requested)
        {
            _registry.Unsubscribe(raw, connection.ConnectionId);
        }
        EnqueueOrShutdown(connection, ServerAckFrame.ForUnsubscribe(frame.Id ?? string.Empty, requested));
    }

    private void HandlePing(SubscriptionConnection connection, ClientFrame frame)
    {
        EnqueueOrShutdown(connection, ServerPongFrame.For(frame.Id ?? string.Empty, frame.Ts ?? 0L));
    }

    private void EnqueueOrShutdown<T>(SubscriptionConnection connection, T frame)
    {
        var bytes = SubscriptionProtocol.Serialize(frame);
        if (!connection.TryEnqueue(bytes))
        {
            _logger.LogWarning(
                "Subscription connection {ConnectionId} outbound queue full on control frame; requesting shutdown.",
                connection.ConnectionId);
            connection.RequestShutdown();
        }
    }
}
