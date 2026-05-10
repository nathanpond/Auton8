using System.Net;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RecordHistoryEnforcementTests
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

    private static async Task GrantViewAsync(AutoNateWebApplicationFactory factory, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            Actions.View, selector, "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task History_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/records/{recordId}/history");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task History_WithRecordView_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var recordId = await SeedRecordAsync(factory);
        await GrantViewAsync(factory, "/record/*");

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/records/{recordId}/history");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
