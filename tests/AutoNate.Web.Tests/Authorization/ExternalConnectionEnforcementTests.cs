using System.Net;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.ExternalConnections;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class ExternalConnectionEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static JsonElement EmptyJson()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static async Task<Guid> SeedConnectionAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalConnectionStore>();
        var row = await store.CreateAsync(
            new CreateExternalConnectionInput(
                Kind: "LlmProvider:Anthropic",
                Name: "test",
                Description: null,
                IsEnabled: true,
                Metadata: EmptyJson(),
                Secret: "stub"),
            AdminUserId);
        return row.Id;
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string action, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, selector, "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task ListConnections_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/external-connections/");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetConnection_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var connectionId = await SeedConnectionAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/external-connections/{connectionId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetConnection_WithViewGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var connectionId = await SeedConnectionAsync(factory);
        await GrantAsync(factory, Actions.View, "/externalconnection/*");
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/external-connections/{connectionId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteConnection_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var connectionId = await SeedConnectionAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/external-connections/{connectionId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task SetDefault_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var connectionId = await SeedConnectionAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync(
            $"/api/external-connections/{connectionId}/set-default", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
