using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RecordEdgeTypeReadEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    // Two record types, two edge types: one restricted to lead-only, one unrestricted.
    private static async Task<(Guid leadId, Guid dealId, Guid leadOnlyEdgeId, Guid universalEdgeId)>
        SeedAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var typeStore = scope.ServiceProvider.GetRequiredService<IRecordTypeStore>();
        var edgeTypeStore = scope.ServiceProvider.GetRequiredService<IRecordEdgeTypeStore>();

        var lead = await typeStore.CreateAsync(
            new CreateRecordTypeInput("lead", "Lead", null, null, null), AdminUserId);
        var deal = await typeStore.CreateAsync(
            new CreateRecordTypeInput("deal", "Deal", null, null, null), AdminUserId);

        var leadOnly = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "leadrel", "Lead Relation", null, IsDirected: true, AllowSelfReference: false,
            Cardinality: "many_to_many",
            FromRecordTypeIds: new[] { lead.Id },
            ToRecordTypeIds: new[] { lead.Id }));
        var universal = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "anyrel", "Any Relation", null, IsDirected: true, AllowSelfReference: false,
            Cardinality: "many_to_many",
            FromRecordTypeIds: null,
            ToRecordTypeIds: null));

        return (lead.Id, deal.Id, leadOnly.Id, universal.Id);
    }

    private static async Task GrantViewAsync(
        AutoNateWebApplicationFactory factory, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            Actions.View, selector, "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task List_NoRecordTypeView_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/record-edge-types/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var edges = await resp.Content.ReadFromJsonAsync<EdgeTypeDto[]>();
        Assert.NotNull(edges);
        Assert.Empty(edges);
    }

    [Fact]
    public async Task List_ViewAllRecordTypes_ReturnsAllEdges()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await SeedAsync(factory);
        await GrantViewAsync(factory, "/recordtype/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/record-edge-types/");
        resp.EnsureSuccessStatusCode();
        var edges = await resp.Content.ReadFromJsonAsync<EdgeTypeDto[]>();
        Assert.NotNull(edges);
        Assert.Equal(2, edges.Length);
    }

    [Fact]
    public async Task List_ViewLeadOnly_HidesDealOnlyEdges_AndShowsUniversal()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (_, _, leadOnlyEdgeId, universalEdgeId) = await SeedAsync(factory);
        await GrantViewAsync(factory, "/recordtype/*[shortcode=lead]");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/record-edge-types/");
        resp.EnsureSuccessStatusCode();
        var edges = await resp.Content.ReadFromJsonAsync<EdgeTypeDto[]>();
        Assert.NotNull(edges);
        var ids = edges.Select(e => e.Id).ToHashSet();
        Assert.Contains(leadOnlyEdgeId, ids);
        Assert.Contains(universalEdgeId, ids);
    }

    [Fact]
    public async Task Get_NotVisible_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (_, _, leadOnlyEdgeId, _) = await SeedAsync(factory);

        // No record-type View grant: lead-only edge type should be hidden.
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-edge-types/{leadOnlyEdgeId}");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Visible_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (_, _, leadOnlyEdgeId, _) = await SeedAsync(factory);
        await GrantViewAsync(factory, "/recordtype/*[shortcode=lead]");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-edge-types/{leadOnlyEdgeId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Fields_NotVisible_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (_, _, leadOnlyEdgeId, _) = await SeedAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/record-edge-types/{leadOnlyEdgeId}/fields");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
