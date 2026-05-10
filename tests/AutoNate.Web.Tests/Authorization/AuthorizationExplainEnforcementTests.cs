using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class AuthorizationExplainEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task Explain_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/admin/explain/",
            new AuthorizationExplainEndpoints.ExplainRequest(
                AsUserId: Guid.NewGuid().ToString(),
                Action: Actions.View,
                TargetKind: EntityKinds.Record,
                TargetId: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Explain_WithSiteConfigView_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            await grants.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, AdminUserId.ToString(),
                Actions.View, "/siteconfig/*", "allow", 0), AdminUserId);
        }

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/admin/explain/",
            new AuthorizationExplainEndpoints.ExplainRequest(
                AsUserId: Guid.NewGuid().ToString(),
                Action: Actions.View,
                TargetKind: EntityKinds.Record,
                TargetId: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
