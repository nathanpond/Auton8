using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.BusWatcher.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 2 first cut: validates the scoped /ws/bus-watcher path end-to-end via
// the live SubscriptionManager (notification + firehose channels only). Drives
// bus messages via BusWatcherStreamService.PublishAsync — the in-process
// bridge wired by Program.cs routes them through the manager's fan-out.
[Trait("Category", "Integration")]
public sealed class SubscriptionManagerTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string Route = "/ws/bus-watcher";

    private static readonly IReadOnlyDictionary<string, string?> AuthOn = new Dictionary<string, string?>
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.ReadOnly,
    };

    [Fact]
    public async Task Subscribe_FirehoseAll_NonSuperAdmin_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "sub-1", new[] { "firehose:all" }, cts.Token);

        Assert.False(ack.TryGetProperty("subscribed", out _));
        var rejected = ack.GetProperty("rejected");
        Assert.Equal(1, rejected.GetArrayLength());
        Assert.Equal("firehose:all", rejected[0].GetProperty("channel").GetString());
        Assert.Equal("forbidden", rejected[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Subscribe_FirehoseAll_SuperAdmin_DeliversEvents()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);
        await PromoteToSuperAdmin(factory);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "sub-2", new[] { "firehose:all" }, cts.Token);
        AssertSubscribed(ack, "firehose:all");

        await PublishBusMessageAsync(factory, "any.topic", "{\"hello\":\"world\"}");

        var evt = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("event", evt.GetProperty("type").GetString());
        Assert.Equal("firehose:all", evt.GetProperty("channel").GetString());
        Assert.Equal("any.topic", evt.GetProperty("topic").GetString());
    }

    [Fact]
    public async Task Subscribe_NotificationUser_OwnId_DeliversMatchingPayload()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var channel = $"notification:user:{AdminUserId}";
        var ack = await SubscribeAsync(ws, "sub-3", new[] { channel }, cts.Token);
        AssertSubscribed(ack, channel);

        var payload = JsonSerializer.Serialize(new
        {
            notificationId = Guid.NewGuid(),
            userId = AdminUserId,
            kind = "info",
            title = "Hi",
            body = "there",
        });
        await PublishBusMessageAsync(factory, "notification.events", payload);

        var evt = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("event", evt.GetProperty("type").GetString());
        Assert.Equal(channel, evt.GetProperty("channel").GetString());
        Assert.Equal("notification.events", evt.GetProperty("topic").GetString());
    }

    [Fact]
    public async Task Subscribe_NotificationUser_OwnId_DoesNotDeliverOtherUsersPayload()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var channel = $"notification:user:{AdminUserId}";
        var ack = await SubscribeAsync(ws, "sub-4", new[] { channel }, cts.Token);
        AssertSubscribed(ack, channel);

        var otherUser = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            notificationId = Guid.NewGuid(),
            userId = otherUser,
            kind = "info",
            title = "Hi",
            body = "there",
        });
        await PublishBusMessageAsync(factory, "notification.events", payload);

        // Should never arrive — resolver targets notification:user:{otherUser},
        // which is a different channel name than what we subscribed to.
        await AssertNoFrameAsync(ws);
    }

    [Fact]
    public async Task Subscribe_NotificationUser_OtherId_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var otherUser = Guid.NewGuid();
        var ack = await SubscribeAsync(ws, "sub-5", new[] { $"notification:user:{otherUser}" }, cts.Token);

        var rejected = ack.GetProperty("rejected");
        Assert.Equal(1, rejected.GetArrayLength());
        Assert.Equal("forbidden", rejected[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Subscribe_Malformed_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "sub-6", new[] { "::nonsense::" }, cts.Token);

        var rejected = ack.GetProperty("rejected");
        Assert.Equal(1, rejected.GetArrayLength());
        Assert.Equal("malformed", rejected[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task IamMutationEvent_BroadcastsInvalidateToSubscribedConnections()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);
        await PromoteToSuperAdmin(factory);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "auth-1", new[] { "firehose:all" }, cts.Token);
        AssertSubscribed(ack, "firehose:all");

        // Drive an iam.events mutation through the bus. AuthChangeListener
        // recognizes the event type, asks SubscriptionManager to broadcast an
        // invalidate listing every currently-subscribed channel, and rebuilds
        // the per-connection snapshot. The firehose subscription also sees
        // this event arrive as a normal event frame; the invalidate is the
        // one we're after.
        var payload = JsonSerializer.Serialize(new
        {
            eventType = IamEventTypes.PermissionGrantCreated,
            resourceKind = IamResourceKinds.PermissionGrant,
            resource = new { id = Guid.NewGuid() },
        });
        await PublishBusMessageAsync(factory, IamEventTopic.TopicName, payload);

        // The event frame and the invalidate arrive in indeterminate order
        // because both come from concurrent in-process subscribers. Look for
        // the invalidate among the first few frames.
        JsonElement? invalidate = null;
        for (var i = 0; i < 4 && invalidate is null; i++)
        {
            var frame = await ReceiveJsonAsync(ws, cts.Token);
            if (frame.GetProperty("type").GetString() == "invalidate")
            {
                invalidate = frame;
            }
        }

        Assert.NotNull(invalidate);
        var channelNames = invalidate!.Value.GetProperty("channels")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("firehose:all", channelNames);
        Assert.Equal("permission-grant.changed", invalidate.Value.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task IamViewEvent_DoesNotTriggerInvalidate()
    {
        // View events (e.g. iam.user.viewed) are NOT in the mutation set, so
        // they shouldn't kick off an invalidate broadcast. Pins the
        // AuthChangeListener filter so adding a new view event type doesn't
        // accidentally trigger an O(connections) rebuild storm.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);
        await PromoteToSuperAdmin(factory);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        var ack = await SubscribeAsync(ws, "auth-2", new[] { "firehose:all" }, cts.Token);
        AssertSubscribed(ack, "firehose:all");

        var payload = JsonSerializer.Serialize(new
        {
            eventType = IamEventTypes.UserViewed,
            resourceKind = IamResourceKinds.User,
            resource = new { id = Guid.NewGuid() },
        });
        await PublishBusMessageAsync(factory, IamEventTopic.TopicName, payload);

        // We will see the firehose event for the iam.user.viewed message itself,
        // but no invalidate should follow. Drain a few frames and verify none
        // is an invalidate.
        for (var i = 0; i < 3; i++)
        {
            try
            {
                using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                var frame = await ReceiveJsonAsync(ws, shortCts.Token);
                Assert.NotEqual("invalidate", frame.GetProperty("type").GetString());
            }
            catch (OperationCanceledException)
            {
                return; // No more frames — confirmed no invalidate.
            }
        }
    }

    [Fact]
    public async Task Disconnect_ClearsRegistryIndices()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);
        var registry = factory.Services.GetRequiredService<SubscriptionRegistry>();
        using var cts = TestTimeout();

        {
            using var ws = await ConnectAsync(factory);
            var ack = await SubscribeAsync(ws, "d-1", new[] { $"notification:user:{AdminUserId}" }, cts.Token);
            AssertSubscribed(ack, $"notification:user:{AdminUserId}");
            Assert.True(registry.ChannelCount >= 1);
            Assert.True(registry.ConnectionCount >= 1);
        } // ws disposes → server read loop exits → finally clears registry

        // Cleanup is async (AcceptAsync's finally block runs after Task.WhenAll
        // unblocks); poll briefly rather than racing it.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while ((registry.ChannelCount != 0 || registry.ConnectionCount != 0)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
        Assert.Equal(0, registry.ChannelCount);
        Assert.Equal(0, registry.ConnectionCount);
    }

    // Slow-consumer backpressure is enforced by SubscriptionConnection's
    // bounded outbound channel: when TryEnqueue returns false,
    // SubscriptionManager.PublishAsync calls RequestShutdown, which cascades
    // through the read/write loops to AcceptAsync's finally block and removes
    // the connection from the registry. There's no integration test for this
    // path because TestHost's in-memory WebSocket buffers don't model real
    // network backpressure reliably (frames drain into TestWebSocket faster
    // than they're published, so TryEnqueue never fails under TestHost).

    [Fact]
    public async Task Ping_RoundTripsAsPong()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        using var ws = await ConnectAsync(factory);
        using var cts = TestTimeout();

        await SendJsonAsync(ws, new { type = "ping", id = "p-1", ts = 12345L }, cts.Token);

        var pong = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("pong", pong.GetProperty("type").GetString());
        Assert.Equal("p-1", pong.GetProperty("id").GetString());
        Assert.Equal(12345L, pong.GetProperty("ts").GetInt64());
    }

    // ---------- helpers ----------

    // Budget for a WebSocket receive. This is a hang guard, not a latency
    // assertion: the tests assert on what arrives, never on how fast. At the
    // old fixed 5 s it failed on most *full-suite* runs — the subscribe ack
    // simply did not land inside the budget while ~1,450 tests contended for
    // the machine — while passing every time in isolation, which is the worst
    // possible signal (archived-163). 30 s is still far below any real hang, and
    // AUTONATE_TEST_WS_TIMEOUT_SECONDS lets a slower CI box raise it without
    // touching code.
    private static CancellationTokenSource TestTimeout()
    {
        var configured = Environment.GetEnvironmentVariable("AUTONATE_TEST_WS_TIMEOUT_SECONDS");
        var seconds = int.TryParse(configured, out var parsed) && parsed > 0 ? parsed : 30;
        return new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    }

    private static async Task<WebSocket> ConnectAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, Route);
        return await client.ConnectAsync(uri, CancellationToken.None);
    }

    private static async Task PromoteToSuperAdmin(AutoNateWebApplicationFactory factory)
    {
        var assignments = factory.Database.CreateRoleAssignmentStore();
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            SystemRoles.SuperAdminId, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);
    }

    private static Task PublishBusMessageAsync(AutoNateWebApplicationFactory factory, string topic, string payload)
    {
        var bus = factory.Services.GetRequiredService<BusWatcherStreamService>();
        var message = new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            topic,
            "application/json",
            new Dictionary<string, string>(),
            payload);
        return bus.PublishAsync(message, CancellationToken.None);
    }

    private static async Task<JsonElement> SubscribeAsync(
        WebSocket ws, string id, IReadOnlyList<string> channels, CancellationToken ct)
    {
        await SendJsonAsync(ws, new { type = "subscribe", id, channels }, ct);
        var ack = await ReceiveJsonAsync(ws, ct);
        Assert.Equal("ack", ack.GetProperty("type").GetString());
        Assert.Equal(id, ack.GetProperty("id").GetString());
        return ack;
    }

    private static void AssertSubscribed(JsonElement ack, string channel)
    {
        var subscribed = ack.GetProperty("subscribed");
        Assert.Contains(channel, subscribed.EnumerateArray().Select(e => e.GetString()!));
        // Should be no rejections.
        Assert.False(ack.TryGetProperty("rejected", out _));
    }

    private static async Task AssertNoFrameAsync(WebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            var frame = await ReceiveJsonAsync(ws, cts.Token);
            Assert.Fail($"expected no frame but received: {frame.GetRawText()}");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<JsonElement> ReceiveJsonAsync(WebSocket ws, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            await ms.WriteAsync(buffer.AsMemory(0, result.Count), ct);
        } while (!result.EndOfMessage);
        var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
        return doc.RootElement.Clone();
    }
}
