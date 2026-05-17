using System.Net.WebSockets;
using AutoNate.Web.Authorization;
using Xunit;

namespace AutoNate.Web.Tests;

// Endpoint-level auth for /ws/bus-watcher. The SubscriptionManager handles
// per-channel authorization (covered by SubscriptionManagerTests and
// BusSubscriptionChannelTests); these tests just pin that the endpoint
// itself requires authentication and accepts every other shape.
[Trait("Category", "Integration")]
public sealed class BusWatcherWebSocketAuthTests
{
    private const string WebSocketRoute = "/ws/bus-watcher";

    private static readonly IReadOnlyDictionary<string, string?> AuthOnNoAutoLogin = new Dictionary<string, string?>
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        // The default factory turns auto-login on so authenticated paths Just
        // Work; disable it here so the test client makes an unauthenticated
        // request and can observe the 401.
        ["DevelopmentAutoLogin:Enabled"] = "false",
    };

    private static readonly IReadOnlyDictionary<string, string?> AuthOn = new Dictionary<string, string?>
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
    };

    [Fact]
    public async Task WebSocket_Anonymous_Rejected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOnNoAutoLogin);

        var wsClient = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, WebSocketRoute);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wsClient.ConnectAsync(uri, CancellationToken.None));

        // .RequireAuthorization() runs the cookie-auth challenge for an
        // anonymous request, which returns 302 (redirect to login) — not 401.
        // Either response means "auth handshake didn't produce a 101 upgrade,"
        // which is the security guarantee we care about here.
        Assert.Contains("302", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebSocket_AuthenticatedUser_Connects()
    {
        // No SuperAdmin promotion — the endpoint accepts any authenticated
        // actor now that per-channel gates do the real authorization.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(AuthOn);

        var wsClient = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, WebSocketRoute);

        using var ws = await wsClient.ConnectAsync(uri, CancellationToken.None);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task WebSocket_AuthorizationDisabled_Connects()
    {
        // The dev/test default — Authorization:Enabled=false. ActorAuthSnapshot
        // treats every authenticated actor as full-access in this mode so the
        // SubscriptionManager accepts the upgrade.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var wsClient = factory.Server.CreateWebSocketClient();
        var uri = new Uri(factory.Server.BaseAddress, WebSocketRoute);

        using var ws = await wsClient.ConnectAsync(uri, CancellationToken.None);
        Assert.Equal(WebSocketState.Open, ws.State);
    }
}
