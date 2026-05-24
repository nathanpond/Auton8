using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Covers `RequirePermission(EntityKinds.WorkflowModel, ...)` on the lifecycle
// endpoints under /api/workflows:
//   - POST   /{id}/publish  → Actions.Publish (WorkflowEndpoints.cs:221)
//   - POST   /{id}/pause    → Actions.Pause   (WorkflowEndpoints.cs:318)
//   - POST   /{id}/resume   → Actions.Pause   (WorkflowEndpoints.cs:345; same action
//                              so a single grant covers the pause/resume pair —
//                              this test pins that intentional sharing)
//   - DELETE /{id}          → Actions.Delete  (WorkflowEndpoints.cs:291)
//
// Start has its own enforcement test (WorkflowStartEnforcementTests). The
// other (Kind, Action) pairs on WorkflowModel — View, Edit — are exercised
// indirectly by WorkflowBehaviorCatalogEnforcementTests (Edit) and the
// model-list endpoints (View) but don't have dedicated lifecycle tests yet.
[Trait("Category", "Integration")]
public sealed class WorkflowLifecycleEnforcementTests
{
    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task Publish_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/workflows/{Guid.NewGuid()}/publish",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Pause_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/workflows/{Guid.NewGuid()}/pause",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Resume_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            $"/api/workflows/{Guid.NewGuid()}/resume",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/workflows/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
