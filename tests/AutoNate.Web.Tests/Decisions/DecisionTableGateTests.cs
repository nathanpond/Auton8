using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AutoNate.Web.Tests.Decisions;

/// <summary>
/// The new <c>decisiontable</c> kind is genuinely enforced, not merely advertised (#110).
/// </summary>
/// <remarks>
/// <para>
/// A new <c>EntityKind</c> needs two DI registrations that decide allow/deny — an
/// <c>IInstanceAuthorizer</c> and an <c>ISelectorCompiler</c> — and the
/// <c>add-permission-gate</c> skill records that pair as having <b>shipped missing
/// five times</b>. Without the authorizer, every instance check denies everyone but
/// super-admins, silently and with no startup error; under
/// <c>Authorization:DryRun=true</c> it does the opposite and allows everyone.
/// </para>
/// <para>
/// <b>These hit an INSTANCE-level route with a concrete id, deliberately.</b> A
/// kind-level route returns from <c>AuthorizeKindLevelAsync</c> <i>before</i> the
/// instance-handler lookup, so a <c>/decisiontable/*</c> grant passes with zero
/// instance authorizers registered — the exact failure the registration exists to
/// prevent, invisible to the obvious test.
/// </para>
/// <para>
/// <b>The factory defaults authorization OFF.</b> Without the three-key override
/// below, the ungranted user gets 200 and this file is a green test proving nothing.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DecisionTableGateTests
{
    private const string Password = "p@ssword123";

    private static readonly Dictionary<string, string?> Enforcing = new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = "full",
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
    };

    [Fact]
    public async Task A_grant_on_one_table_does_not_reach_another()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Enforcing);
        var admin = factory.CreateClient();
        (await admin.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var granted = await CreateTableAsync(admin, "granted_table");
        var other = await CreateTableAsync(admin, "other_table");

        var user = await CreateUserAsync(admin, "author-" + Guid.NewGuid().ToString("N")[..8]);
        await GrantAsync(admin, user.UserId, "view", $"/decisiontable/{granted.Id}");

        var client = await SignInAsync(factory, user.Username);

        // The granted one: reachable. If the IInstanceAuthorizer were missing this
        // would be 403 -- the five-times failure -- and nothing else in the suite
        // would notice.
        var allowed = await client.GetAsync($"/api/decision-tables/{granted.Id}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // The other one: refused. Without this half, a compiler that matched
        // everything would pass the line above and the grant would mean nothing.
        var refused = await client.GetAsync($"/api/decision-tables/{other.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Viewing_does_not_carry_editing_or_publishing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Enforcing);
        var admin = factory.CreateClient();
        (await admin.GetAsync("/api/decision-tables/")).EnsureSuccessStatusCode();

        var table = await CreateTableAsync(admin, "readonly_table");
        var user = await CreateUserAsync(admin, "reader-" + Guid.NewGuid().ToString("N")[..8]);
        await GrantAsync(admin, user.UserId, "view", $"/decisiontable/{table.Id}");

        var client = await SignInAsync(factory, user.Username);

        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/decision-tables/{table.Id}")).StatusCode);

        // A table decides business outcomes, so "may read" must not imply "may
        // change what it decides". Each verb is checked separately, because one
        // gate wired to the wrong action would still pass a test that only
        // granted everything.
        var edited = await client.PutAsJsonAsync($"/api/decision-tables/{table.Id}", Sample("readonly_table"));
        Assert.Equal(HttpStatusCode.Forbidden, edited.StatusCode);

        var published = await client.PostAsync($"/api/decision-tables/{table.Id}/publish", null);
        Assert.Equal(HttpStatusCode.Forbidden, published.StatusCode);

        var deleted = await client.DeleteAsync($"/api/decision-tables/{table.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
    }

    // ── harness ─────────────────────────────────────────────────────────────

    private static DecisionTableModel Sample(string key) => new()
    {
        DecisionKey = key,
        Name = "Routing",
        HitPolicy = DecisionHitPolicies.First,
        Inputs = [new DecisionColumn("in_amount", "Amount", "amount", DecisionTypeRefs.Number)],
        Outputs = [new DecisionColumn("out_route", "Route", "route", DecisionTypeRefs.String)],
        Rules = [new DecisionRule("r1", ["> 10"], ["\"escalate\""])]
    };

    private static async Task<DecisionTableModel> CreateTableAsync(HttpClient admin, string key)
    {
        var response = await admin.PostAsJsonAsync("/api/decision-tables/", Sample(key));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DecisionTableModel>()
            ?? throw new InvalidOperationException("Create returned no body.");
    }

    private static async Task<UserDto> CreateUserAsync(HttpClient admin, string username)
    {
        var response = await admin.PostAsJsonAsync("/api/users",
            new UserEndpoints.CreateUserRequest(
                Username: username, FirstName: "Deci", LastName: "Sion",
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
        // place and everything after runs as super-admin, passing every assertion
        // while proving nothing about the account it meant to use.
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.False(
            (login.Headers.Location?.ToString() ?? string.Empty)
                .Contains("error=", StringComparison.Ordinal),
            "login failed; everything after this would have run as the bootstrap admin.");

        return client;
    }

    private sealed record UserDto(long Id, Guid UserId, string Username);

    private sealed record AntiforgeryTokenDto(string Token, string FormFieldName, string HeaderName);
}
