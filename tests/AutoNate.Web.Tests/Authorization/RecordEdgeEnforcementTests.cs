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
public sealed class RecordEdgeEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
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

    // Seeds two records and a directed edge type connecting them. Stores are
    // resolved through DI, so seeding bypasses the HTTP auth gates we're
    // exercising in these tests.
    private static async Task<(Guid recordA, Guid recordB, Guid edgeTypeId)> SeedAsync(
        AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var typeStore = scope.ServiceProvider.GetRequiredService<IRecordTypeStore>();
        var recordStore = scope.ServiceProvider.GetRequiredService<IRecordStore>();
        var edgeTypeStore = scope.ServiceProvider.GetRequiredService<IRecordEdgeTypeStore>();

        var rt = await typeStore.CreateAsync(
            new CreateRecordTypeInput("task", "Task", null, null, null), AdminUserId);
        var a = await recordStore.CreateAsync(
            new CreateRecordInput(rt.Id, "A", null, null, EmptyJson(), null), AdminUserId);
        var b = await recordStore.CreateAsync(
            new CreateRecordInput(rt.Id, "B", null, null, EmptyJson(), null), AdminUserId);
        var edgeType = await edgeTypeStore.CreateAsync(new CreateRecordEdgeTypeInput(
            "rel", "Relates", null, IsDirected: true, AllowSelfReference: false,
            Cardinality: "many_to_many", null, null));
        return (a.Id, b.Id, edgeType.Id);
    }

    private static async Task<Guid> SeedEdgeAsync(
        AutoNateWebApplicationFactory factory,
        Guid edgeTypeId, Guid fromRecordId, Guid toRecordId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var edges = scope.ServiceProvider.GetRequiredService<IRecordEdgeStore>();
        var edge = await edges.CreateAsync(
            new CreateRecordEdgeInput(edgeTypeId, fromRecordId, toRecordId, EmptyJson()),
            AdminUserId);
        return edge.Id;
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

    private static async Task GrantEditAsync(
        AutoNateWebApplicationFactory factory, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            Actions.Edit, selector, "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task CreateEdge_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, edgeTypeId) = await SeedAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/record-edges/",
            new CreateEdgeRequest(edgeTypeId, a, b, EmptyJson()));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateEdge_OnlyOneEndpointEditable_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, edgeTypeId) = await SeedAsync(factory);
        await GrantEditAsync(factory, $"/record/{a}");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/record-edges/",
            new CreateEdgeRequest(edgeTypeId, a, b, EmptyJson()));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateEdge_BothEndpointsEditable_Returns201()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, edgeTypeId) = await SeedAsync(factory);
        await GrantEditAsync(factory, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/record-edges/",
            new CreateEdgeRequest(edgeTypeId, a, b, EmptyJson()));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteEdge_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, edgeTypeId) = await SeedAsync(factory);
        var edgeId = await SeedEdgeAsync(factory, edgeTypeId, a, b);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/record-edges/{edgeId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteEdge_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/record-edges/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ListEdges_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, _, _) = await SeedAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/records/{a}/edges");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ListEdges_FiltersEdgesToHiddenRecords()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, edgeTypeId) = await SeedAsync(factory);
        await SeedEdgeAsync(factory, edgeTypeId, a, b);

        // Only A is visible; the edge points at B which is not.
        await GrantViewAsync(factory, $"/record/{a}");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var edges = await client.GetFromJsonAsync<EdgeDto[]>($"/api/records/{a}/edges");

        Assert.NotNull(edges);
        Assert.Empty(edges);
    }

    [Fact]
    public async Task Traverse_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, _, _) = await SeedAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/records/{a}/traverse",
            new TraverseHttpRequest(Array.Empty<Guid>(), null, "outgoing", 1));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Traverse_FiltersHiddenRecords()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, edgeTypeId) = await SeedAsync(factory);
        await SeedEdgeAsync(factory, edgeTypeId, a, b);

        // Actor can View A but not B.
        await GrantViewAsync(factory, $"/record/{a}");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/records/{a}/traverse",
            new TraverseHttpRequest(Array.Empty<Guid>(), null, "outgoing", 1));
        resp.EnsureSuccessStatusCode();
        var rows = await resp.Content.ReadFromJsonAsync<TraverseResultDto[]>();

        Assert.NotNull(rows);
        Assert.Contains(rows, r => r.RecordId == a);
        Assert.DoesNotContain(rows, r => r.RecordId == b);
    }

    [Fact]
    public async Task Traverse_StartIdsBypass_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var (a, b, _) = await SeedAsync(factory);

        // Actor can View A (route id) but not B (the StartRecordIds entry).
        await GrantViewAsync(factory, $"/record/{a}");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/records/{a}/traverse",
            new TraverseHttpRequest(new[] { b }, null, "outgoing", 1));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
