using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Retry and reschedule are grantable independently (#172).
/// </summary>
/// <remarks>
/// <para>
/// The AC asks for this outright — <i>"Retry and reschedule are separately
/// grantable, so read-only operator access is a real option"</i> — and it is the
/// half a single happy-path test cannot see. One gate wired to the wrong
/// <c>(kind, action)</c> pair, or both wired to the same one, passes every test
/// that only grants everything and checks a 204.
/// </para>
/// <para>
/// So each grant is tested against <b>both</b> endpoints: the granted one must
/// pass the gate and the other must be refused. That is four assertions, and no
/// three of them would catch a shared gate.
/// </para>
/// <para>
/// <b>The factory defaults authorization OFF.</b> Without the three-key override
/// below, an ungranted user gets 200 and this file would be a green test proving
/// nothing — the failure the <c>add-permission-gate</c> skill calls out by name.
/// </para>
/// <para>
/// The engine is not involved: the assertions are on whether the request got
/// PAST the gate, not on what it then did. A request that clears the gate reaches
/// a handler that will fail to reach Flowable, so "not 403" is the signal, and
/// asserting a 204 here would make this an engine test that cannot run in slim.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class JobGateIndependenceTests
{
    private const string Password = "p@ssword123";

    [Theory]
    // granted action                 may retry  may reschedule
    [InlineData("retryjob", true, false)]
    [InlineData("reschedulejob", false, true)]
    public async Task One_job_grant_does_not_carry_the_other(
        string grantedAction, bool mayRetry, bool mayReschedule)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                ["Authorization:Enabled"] = "true",
                ["Authorization:Enforcement"] = "full",
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });

        var admin = factory.CreateClient();
        (await admin.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        // A CACHED execution row, fresh. WorkflowExecutionInstanceAuthorizer
        // refuses an instance it cannot find -- correctly, since with no facts no
        // selector can be evaluated -- so without this every row here would be
        // 403 for a reason that has nothing to do with the gates under test, and
        // the negative halves would pass for the wrong reason.
        //
        // Fresh rather than aged, unlike ExecutionAuthorizationFromCacheTests:
        // those force a live read to prove the degradation path, and this must
        // NOT reach Flowable, which slim does not stand up.
        var instanceId = "inst-gate-" + Guid.NewGuid().ToString("N")[..8];
        await SeedCachedInstanceAsync(factory, instanceId);

        var user = await CreateUserAsync(admin, "op-" + Guid.NewGuid().ToString("N")[..8]);

        // View as well, because both endpoints sit under a group whose other
        // members need it -- the point of the test is the second gate, not the
        // first, and withholding View would make every row 403 for the same
        // uninteresting reason.
        await GrantAsync(admin, user.UserId, "view", $"/workflowexecution/{instanceId}");
        await GrantAsync(admin, user.UserId, grantedAction, $"/workflowexecution/{instanceId}");

        var client = await SignInAsync(factory, user.Username);

        var retry = await client.PostAsJsonAsync(
            $"/api/executions/{instanceId}/jobs/job-1/retry",
            new ExecutionEndpoints.RetryJobRequest("deadletter"));

        var reschedule = await client.PostAsJsonAsync(
            $"/api/executions/{instanceId}/jobs/job-1/reschedule",
            new ExecutionEndpoints.RescheduleJobRequest(DateTimeOffset.UtcNow.AddMinutes(1)));

        AssertGate("retry", retry.StatusCode, mayRetry, grantedAction);
        AssertGate("reschedule", reschedule.StatusCode, mayReschedule, grantedAction);
    }

    /// <summary>
    /// 403 means the gate refused. Anything else means it did not — including the
    /// 500 a granted request earns by reaching a Flowable that is not there,
    /// which is the correct outcome for this test and a wrong one to assert 204
    /// against.
    /// </summary>
    private static void AssertGate(string what, HttpStatusCode actual, bool expectedAllowed, string granted)
    {
        if (expectedAllowed)
        {
            Assert.True(
                actual != HttpStatusCode.Forbidden,
                $"'{granted}' was granted, but {what} was refused by the gate (403). "
                + "The endpoint is wired to the wrong (kind, action) pair.");
        }
        else
        {
            Assert.True(
                actual == HttpStatusCode.Forbidden,
                $"'{granted}' was granted and {what} went through anyway ({(int)actual}). "
                + "The two gates are not independent -- one grant is carrying both.");
        }
    }

    private static async Task SeedCachedInstanceAsync(
        AutoNateWebApplicationFactory factory, string instanceId)
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, instanceId,
                new WorkflowExecutionSummary
                {
                    Id = instanceId,
                    Name = "gate test run",
                    ProcessDefinitionId = "gates:1:1",
                    Status = "running",
                    StartUserId = "someone-else",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    LastActivityAtUtc = DateTimeOffset.UtcNow
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);
    }

    private static async Task<UserDto> CreateUserAsync(HttpClient admin, string username)
    {
        var response = await admin.PostAsJsonAsync("/api/users",
            new UserEndpoints.CreateUserRequest(
                Username: username, FirstName: "Op", LastName: "Erator",
                Password: Password, Email: username + "@x.com"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserDto>()
            ?? throw new InvalidOperationException("User creation response was empty.");
    }

    private static async Task GrantAsync(HttpClient admin, Guid userId, string action, string selector)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/grants",
            new PermissionGrantEndpoints.CreateGrantRequest(
                PrincipalKind: "user", PrincipalId: userId.ToString(),
                Action: action, SelectorString: selector, Effect: "allow", Priority: 0));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> SignInAsync(
        AutoNateWebApplicationFactory factory, string username)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Clear();

        var tokenResponse = await client.GetAsync("/api/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenDto>()
            ?? throw new InvalidOperationException("Antiforgery token response was empty.");

        var login = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [tokens.FormFieldName] = tokens.Token,
                ["username"] = username,
                ["password"] = Password
            }));

        // Every login failure path also redirects, to /login?error=... -- and the
        // antiforgery GET above already minted a dev auto-login cookie for the
        // bootstrap admin, who holds SuperAdmin. A failed login leaves that in
        // place and everything after this runs as super-admin, passing every
        // assertion while proving nothing about the account it meant to use.
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var location = login.Headers.Location?.ToString() ?? string.Empty;
        Assert.False(
            location.Contains("error=", StringComparison.Ordinal),
            $"login failed: redirected to {location}. Everything after this would have run "
            + "as the auto-login bootstrap admin.");

        return client;
    }

    private sealed record UserDto(long Id, Guid UserId, string Username);

    private sealed record AntiforgeryTokenDto(string Token, string FormFieldName, string HeaderName);
}
