using System.Net;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class WorkflowBehaviorCatalogEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task Catalog_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/workflow-behaviors/");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Catalog_WithWorkflowModelEdit_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            await grants.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, AdminUserId.ToString(),
                Actions.Edit, "/workflowmodel/*", "allow", 0), AdminUserId);
        }

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/workflow-behaviors/");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
