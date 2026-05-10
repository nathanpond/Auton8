using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Covers the supervisor read/write endpoints on UserEndpoints, which use
// instance-level RequirePermission(EntityKinds.User, ...). Without a
// UserInstanceAuthorizer registered the Authorizer would deny these with
// "no instance handler for kind 'user'" when Enforcement=Full — these tests
// pin that wiring in place.
[Trait("Category", "Integration")]
public sealed class UserSupervisorEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task<Guid> SeedSubjectUserAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<ILocalUserStore>();
        var subject = await users.CreateAsync(
            username: "subject", firstName: "Sub", lastName: "Ject",
            password: "Hunter2!", email: "subject@example.com");
        return subject.UserId;
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
    public async Task GetSupervisor_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var subjectId = await SeedSubjectUserAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/users/{subjectId}/supervisor");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetSupervisor_WithUserViewGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var subjectId = await SeedSubjectUserAsync(factory);
        await GrantAsync(factory, Actions.View, "/user/*");
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/users/{subjectId}/supervisor");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task PutSupervisor_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var subjectId = await SeedSubjectUserAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PutAsJsonAsync(
            $"/api/users/{subjectId}/supervisor",
            new UserEndpoints.SetSupervisorRequest(SupervisorUserId: null));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
