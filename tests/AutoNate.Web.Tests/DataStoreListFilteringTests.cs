using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using Xunit;

namespace AutoNate.Web.Tests;

// Asserts the per-store scoping promised by RequirePermission(DataStore, ...)
// extends to the bare list endpoint. Without FilterQueryAsync wired in, a
// user with View on /datastore/<A> would still see /datastore/<B> in the
// list — leaking the existence of stores they can't open.
[Trait("Category", "Integration")]
public sealed class DataStoreListFilteringTests
{
    [Fact]
    public async Task GetDataStores_filters_by_per_store_view_grants()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                // Turn enforcement all the way on so FilterQueryAsync actually
                // narrows the source (Enabled=false / Enforcement=off would
                // short-circuit it to a pass-through).
                ["Authorization:Enabled"] = "true",
                ["Authorization:Enforcement"] = "full",
                // The seeded admin user needs SuperAdmin to perform the
                // create-store + create-user + create-grant setup chain
                // under full enforcement.
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });
        var adminClient = factory.CreateClient();

        // Prime the dev auto-login cookie via a GET; subsequent POSTs from
        // this client carry the admin identity.
        (await adminClient.GetAsync("/api/datastores")).EnsureSuccessStatusCode();

        // Two stores: alice will get View on storeA only.
        var storeA = await CreateFileStoreAsync(adminClient, "store-a-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var storeB = await CreateFileStoreAsync(adminClient, "store-b-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        // Create alice as a non-superadmin user.
        var aliceResp = await adminClient.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest(
                Username: "alice-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                FirstName: "Alice",
                LastName: "Liddell",
                Password: "p@ssword123",
                Email: "alice@x.com"));
        aliceResp.EnsureSuccessStatusCode();
        var alice = await aliceResp.Content.ReadFromJsonAsync<UserDto>()
            ?? throw new InvalidOperationException("User creation response was empty.");

        // Grant alice View on storeA only.
        var grantResp = await adminClient.PostAsJsonAsync(
            "/api/admin/grants",
            new PermissionGrantEndpoints.CreateGrantRequest(
                PrincipalKind: "user",
                PrincipalId: alice.UserId.ToString(),
                Action: "view",
                SelectorString: $"/datastore/{storeA.Id}",
                Effect: "allow",
                Priority: 0));
        grantResp.EnsureSuccessStatusCode();

        // alice logs in on a fresh client. Headers are cleared so no
        // residual auth bleeds in from the test runner; the form-login
        // sets a manual identity that the auto-login middleware leaves
        // alone on subsequent GETs.
        var aliceClient = factory.CreateClient();
        aliceClient.DefaultRequestHeaders.Clear();
        // HttpClient follows the post-login redirect by default and lands on
        // the landing page with 200 OK; the auth cookie is set as a side
        // effect. The list-fetch below is the real verification that alice
        // is authenticated as herself rather than auto-logged-in as admin.
        var loginResp = await PostLoginWithAntiforgeryAsync(aliceClient, alice.Username, "p@ssword123");
        loginResp.EnsureSuccessStatusCode();

        // Alice sees ONLY storeA.
        var aliceList = await aliceClient.GetAsync("/api/datastores");
        aliceList.EnsureSuccessStatusCode();
        var aliceRows = await aliceList.Content.ReadFromJsonAsync<DataStoreDto[]>()
            ?? throw new InvalidOperationException("List response was empty.");
        var only = Assert.Single(aliceRows);
        Assert.Equal(storeA.Id, only.Id);

        // Sanity: admin (SuperAdmin) sees both — proves the test isn't
        // false-positive by hiding stores from everyone.
        var adminList = await adminClient.GetAsync("/api/datastores");
        adminList.EnsureSuccessStatusCode();
        var adminRows = await adminList.Content.ReadFromJsonAsync<DataStoreDto[]>()
            ?? throw new InvalidOperationException("Admin list response was empty.");
        var adminIds = adminRows.Select(r => r.Id).ToHashSet();
        Assert.Contains(storeA.Id, adminIds);
        Assert.Contains(storeB.Id, adminIds);
    }

    private static async Task<DataStoreDto> CreateFileStoreAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/datastores",
            new CreateDataStoreRequest(Name: name, Description: null, Kind: "FileType"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DataStoreDto>()
            ?? throw new InvalidOperationException("Create-store response was empty.");
    }

    // Login submits a form-urlencoded body to /account/login with a CSRF
    // token fetched from /api/auth/antiforgery. Mirrors the SPA flow.
    private static async Task<HttpResponseMessage> PostLoginWithAntiforgeryAsync(
        HttpClient client,
        string username,
        string password)
    {
        var tokenResp = await client.GetAsync("/api/auth/antiforgery");
        tokenResp.EnsureSuccessStatusCode();
        var tokens = await tokenResp.Content.ReadFromJsonAsync<AntiforgeryTokenDto>()
            ?? throw new InvalidOperationException("Antiforgery token response was empty.");
        var fields = new Dictionary<string, string>
        {
            [tokens.FormFieldName] = tokens.Token,
            ["username"] = username,
            ["password"] = password
        };
        return await client.PostAsync("/account/login", new FormUrlEncodedContent(fields));
    }

    private sealed record DataStoreDto(Guid Id, string Name);
    private sealed record UserDto(long Id, Guid UserId, string Username);
    private sealed record AntiforgeryTokenDto(string Token, string FormFieldName, string HeaderName);
}
