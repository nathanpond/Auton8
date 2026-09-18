using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

/// <summary>
/// Broadcasting a signal is its own permission, and a wider one (#523).
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="WorkflowMessageGateTests"/>, and the direction that
/// matters most is the one between those two kinds. The message right advances
/// ONE process the caller names, at a correlation value they supply. This one
/// wakes every waiting instance of every workflow that declares a name. A gate
/// copy-pasted from the message endpoint would let a "may advance a process"
/// grant do the second, and presence-checking cannot see that: both are POSTs
/// carrying a real gate, wired to the wrong kind.
/// </para>
/// <para>
/// <c>KindGateEnforcementTests</c> enumerates GET routes, so it cannot see this
/// one either.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class WorkflowSignalGateTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string Route = "/api/workflow-signals/";

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static object Signal() => new { signalName = "order.cancelled" };

    [Fact]
    public async Task Broadcasting_without_any_grant_is_forbidden()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Signal());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Broadcasting_with_the_signal_grant_is_allowed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowSignal, Actions.Send);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Signal());

        // Not asserting success: with nothing published the broadcast is refused
        // as an unknown signal and answers 404. What is asserted is that the gate
        // OPENED, which a 403 would deny.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The claim the separate kind exists for (#523).
    /// </summary>
    /// <remarks>
    /// An integration granted "may advance a process" must not thereby gain "may
    /// wake every instance in the engine waiting on a name". If this passed, the
    /// two kinds would be one kind with two spellings.
    /// </remarks>
    [Fact]
    public async Task The_message_grant_does_not_open_the_signal_route()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowMessage, Actions.Send);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Signal());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>And the same claim in the other direction.</summary>
    [Fact]
    public async Task The_signal_grant_does_not_open_the_message_route()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowSignal, Actions.Send);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/workflow-messages/",
            new { processKey = "order", messageName = "ShipmentReady", correlationValue = "ORD-1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_execution_operator_who_lacks_the_signal_grant_is_refused()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.Override);
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.View);
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.Delete);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Signal());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string kind, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, $"/{kind}/*", "allow", 0), AdminUserId);
    }

    private static async Task<HttpClient> SignedInClientAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        // Dev auto-login skips POSTs, so land the cookie with a GET first.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return client;
    }
}
