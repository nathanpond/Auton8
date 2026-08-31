using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// #22: GET /api/code-transformers/{id} returned the full source body — for
// unsafe rows too — to any authenticated caller holding a GUID.
// #23: create gated on (Transformer, Run) whatever kind was requested, so a
// grant meant to let someone execute a pipeline node let them author the
// sandboxed code that later runs execute, and analyzer:* was never enforced.
[Trait("Category", "Integration")]
public sealed class CodeTransformerEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task Detail_WithoutViewGrant_DoesNotReturnSource()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Transformer, Actions.Create);
        var client = await SignedInClientAsync(factory);

        var id = await CreateTransformerAsync(client, "secret-body");

        var resp = await client.GetAsync($"/api/code-transformers/{id}");

        // No transformer:view grant — a held GUID must reveal nothing.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.DoesNotContain("secret-body", await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_WithViewGrant_ReturnsSource()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Transformer, Actions.Create);
        await GrantAsync(factory, EntityKinds.Transformer, Actions.View);
        var client = await SignedInClientAsync(factory);

        var id = await CreateTransformerAsync(client, "secret-body");

        var resp = await client.GetAsync($"/api/code-transformers/{id}");

        resp.EnsureSuccessStatusCode();
        Assert.Contains("secret-body", await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // The heart of #23: Run is an execution grant and must not confer authoring.
    [Fact]
    public async Task Create_WithOnlyRunGrant_IsForbidden()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Transformer, Actions.Run);
        var client = await SignedInClientAsync(factory);

        var resp = await PostTransformerAsync(client, "transformer", "x = 1");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithCreateGrant_Succeeds()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Transformer, Actions.Create);
        var client = await SignedInClientAsync(factory);

        var resp = await PostTransformerAsync(client, "transformer", "x = 1");

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    // Kind isolation, the other half of #23: the gate must resolve against the
    // kind actually being created, so a transformer grant is not an analyzer
    // grant and analyzer:* is finally enforceable.
    [Fact]
    public async Task Create_TransformerGrantDoesNotAuthorizeAnalyzer()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Transformer, Actions.Create);
        var client = await SignedInClientAsync(factory);

        var resp = await PostTransformerAsync(client, "analyzer", "y = 2");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Create_AnalyzerGrantAuthorizesAnalyzer()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Analyzer, Actions.Create);
        var client = await SignedInClientAsync(factory);

        var resp = await PostTransformerAsync(client, "analyzer", "y = 2");

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutDeleteGrant_IsNotFound()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        await GrantAsync(factory, EntityKinds.Transformer, Actions.Create);
        var client = await SignedInClientAsync(factory);

        var id = await CreateTransformerAsync(client, "x = 1");

        // Owner, but no transformer:delete grant.
        var resp = await client.DeleteAsync($"/api/code-transformers/{id}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- helpers ----

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

    private static Task<HttpResponseMessage> PostTransformerAsync(
        HttpClient client, string kind, string code) =>
        client.PostAsJsonAsync("/api/code-transformers/", new
        {
            name = "t-" + Guid.NewGuid().ToString("N")[..8],
            description = (string?)null,
            kind,
            language = "python",
            code,
            isUnsafe = false
        });

    private static async Task<Guid> CreateTransformerAsync(HttpClient client, string code)
    {
        var resp = await PostTransformerAsync(client, "transformer", code);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<CreatedDto>();
        Assert.NotNull(body);
        return body!.Id;
    }

    private sealed record CreatedDto(Guid Id);
}
