using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Covers `RequirePermission(EntityKinds.Record, Actions.Edit)` on
// PATCH /api/records/{id} (RecordEndpoints.cs:261) and
// POST /api/records/{id}/restore (RecordEndpoints.cs:297).
//
// Authoring regression net: if either endpoint's gate ever swaps to a wider
// action (e.g. View) or a different kind, the no-grant→403 case will flip.
[Trait("Category", "Integration")]
public sealed class RecordEditEnforcementTests
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

    private static async Task<Guid> SeedRecordAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var typeStore = scope.ServiceProvider.GetRequiredService<IRecordTypeStore>();
        var recordStore = scope.ServiceProvider.GetRequiredService<IRecordStore>();
        var rt = await typeStore.CreateAsync(
            new CreateRecordTypeInput("task", "Task", null, null, null), AdminUserId);
        var record = await recordStore.CreateAsync(
            new CreateRecordInput(rt.Id, "R", null, null, EmptyJson(), null), AdminUserId);
        return record.Id;
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
    public async Task Patch_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PatchAsJsonAsync(
            $"/api/records/{Guid.NewGuid()}",
            new { name = "renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Restore_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/records/{Guid.NewGuid()}/restore",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_WithViewGrantOnly_Returns403()
    {
        // Separation guard: a Record:View grant must NOT satisfy the Edit gate.
        // If Edit ever silently widens to View this test flips to 200/404.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.View, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PatchAsJsonAsync(
            $"/api/records/{recordId}",
            new { name = "renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_WithRecordEditGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.Edit, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PatchAsJsonAsync(
            $"/api/records/{recordId}",
            new { name = "renamed" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Restore_WithRecordEditGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.Edit, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/records/{recordId}/restore",
            new { });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
