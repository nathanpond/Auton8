using System.Net;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Covers `RequirePermission(EntityKinds.Record, Actions.Delete)` on the
// permanent-delete endpoint at RecordEndpoints.cs:319.
//
// The critical guard here is the separation from Archive: the soft-archive
// (DELETE /api/records/{id}) uses Actions.Archive (RecordEndpoints.cs:279)
// while the cascade-purge (DELETE /api/records/{id}/permanent) uses
// Actions.Delete. A Record:Archive or Record:Edit grant must NOT silently
// unlock the irreversible path. The CoreEntityTypes comment at lines 79-83
// makes that separation explicit; this test pins it.
[Trait("Category", "Integration")]
public sealed class RecordDeleteEnforcementTests
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
    public async Task PermanentDelete_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/records/{Guid.NewGuid()}/permanent");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PermanentDelete_WithRecordEditGrantOnly_Returns403()
    {
        // Separation guard. An admin handing out Record:Edit (so a contributor
        // can rename/relabel records) must not silently grant the ability to
        // permanently destroy them. If the permanent-delete gate ever widens
        // to accept Edit, this test flips to 200/404.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.Edit, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/records/{recordId}/permanent");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PermanentDelete_WithRecordArchiveGrantOnly_Returns403()
    {
        // Same separation guard, narrower hand-out. Archive is the routine
        // soft-delete granted to operators. It must not silently authorize
        // the cascade-purge endpoint.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.Archive, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/records/{recordId}/permanent");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PermanentDelete_WithRecordDeleteGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantAsync(factory, Actions.Delete, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/records/{recordId}/permanent");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
