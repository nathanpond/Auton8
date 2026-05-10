using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RecordTypeReadEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task<(Guid lead, Guid deal)> SeedTwoTypesAsync(
        AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var typeStore = scope.ServiceProvider.GetRequiredService<IRecordTypeStore>();
        var lead = await typeStore.CreateAsync(
            new CreateRecordTypeInput("lead", "Lead", null, null, null), AdminUserId);
        var deal = await typeStore.CreateAsync(
            new CreateRecordTypeInput("deal", "Deal", null, null, null), AdminUserId);
        return (lead.Id, deal.Id);
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
    public async Task List_NoGrant_ReturnsEmptyArrayNotForbidden()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedTwoTypesAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/record-types/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var types = await resp.Content.ReadFromJsonAsync<RecordTypeDto[]>();
        Assert.NotNull(types);
        Assert.Empty(types);
    }

    [Fact]
    public async Task List_ScopedGrant_ReturnsOnlyMatchingTypes()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (leadId, _) = await SeedTwoTypesAsync(factory);
        await GrantAsync(factory, Actions.View, "/recordtype/*[shortcode=lead]");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/record-types/");
        resp.EnsureSuccessStatusCode();
        var types = await resp.Content.ReadFromJsonAsync<RecordTypeDto[]>();
        Assert.NotNull(types);
        Assert.Single(types);
        Assert.Equal(leadId, types[0].Id);
    }

    [Fact]
    public async Task Get_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (leadId, _) = await SeedTwoTypesAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-types/{leadId}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Get_WithKindGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (leadId, _) = await SeedTwoTypesAsync(factory);
        await GrantAsync(factory, Actions.View, "/recordtype/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-types/{leadId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Fields_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (leadId, _) = await SeedTwoTypesAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-types/{leadId}/fields");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Audit_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (leadId, _) = await SeedTwoTypesAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-types/{leadId}/audit");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
