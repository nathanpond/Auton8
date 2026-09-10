using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

/// <summary>
/// Sending a message is its own permission (#254).
/// </summary>
/// <remarks>
/// <para>
/// The entire reason #112 introduced <c>WorkflowMessage</c> as an
/// <see cref="EntityKinds"/> rather than an action on <c>WorkflowExecution</c> is
/// that an integration should be able to advance a waiting process **without**
/// holding execution-operator powers over every instance in the system. Nothing
/// proved that independence.
/// </para>
/// <para>
/// <c>KindGateEnforcementTests</c> could not: its cases enumerate GET routes, and
/// this endpoint is a POST, so it was never added. The gate is real and
/// presence-checked — but a future mis-wiring to <c>workflowexecution:*</c> would
/// have passed every test in the suite, and the story's own test plan item ("an
/// actor holding execution-operator permissions but not the new kind is refused")
/// had no test anywhere.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class WorkflowMessageGateTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string Route = "/api/workflow-messages/";

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static object Message() => new
    {
        processKey = "order",
        messageName = "ShipmentReady",
        correlationValue = "ORD-1"
    };

    [Fact]
    public async Task Sending_without_any_grant_is_forbidden()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Message());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sending_with_the_message_grant_is_allowed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowMessage, Actions.Send);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Message());

        // Not asserting success: with no such process published the send has
        // nothing to correlate to and answers 404/409. What is being asserted is
        // that the gate opened, which a 403 would deny.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_execution_operator_who_lacks_the_message_grant_is_refused()
    {
        // The independence claim, in the direction that matters for the gate
        // being wired to the wrong kind. Every one of these actions is one an
        // execution operator holds; none of them may open this route.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.Override);
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.View);
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.Delete);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Message());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_message_grant_alone_does_not_confer_execution_operator_powers()
    {
        // The other direction of the same claim. A separate kind that silently
        // rode along with execution permissions would be no separation at all;
        // so would one that granted them.
        //
        // Overriding an instance's variables is the operator power to test
        // against, not the executions list -- that route gates nothing and
        // filters inside the handler, so an actor with no execution grant gets
        // 200 and an empty list by design.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        factory.FlowableStub.InstancesById["pi-msg"] = new FlowableProcessInstanceSummary
        {
            Id = "pi-msg",
            ProcessDefinitionId = "order:1:abc"
        };

        await GrantAsync(factory, EntityKinds.WorkflowMessage, Actions.Send);
        var client = await SignedInClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/executions/pi-msg/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "x", Value = 1, Type = "integer" }
            }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_send_grant_on_a_different_kind_does_not_open_the_route()
    {
        // Same action, different kind. Presence-checking cannot see this, and it
        // is exactly what a copy-pasted gate looks like.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.WorkflowExecution, Actions.Send);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync(Route, Message());

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
