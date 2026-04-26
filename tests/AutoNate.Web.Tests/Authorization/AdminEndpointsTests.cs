using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class AdminEndpointsTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task RoleLifecycle_CreateAssign_ReflectsOnAuthMe()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(NoBackfill);
        var client = factory.CreateClient();

        // Auto-login fires on the first GET; gives us a session as `admin`.
        var meBefore = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.NotNull(meBefore);
        Assert.True(meBefore!.Authenticated);
        Assert.False(meBefore.IsSuperAdmin);
        Assert.Empty(meBefore.Roles);

        // Create a new role.
        var created = await client.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Editors", Description = "Phase 3 demo role" });
        created.EnsureSuccessStatusCode();
        var role = await created.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(role);

        // Assign it to the admin user.
        var assignBody = new
        {
            PrincipalKind = EntityKinds.User,
            PrincipalId = AdminUserId.ToString(),
            ScopeString = (string?)null
        };
        var assigned = await client.PostAsJsonAsync(
            $"/api/admin/roles/{role!.Id}/assignments", assignBody);
        assigned.EnsureSuccessStatusCode();

        var meAfter = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.NotNull(meAfter);
        Assert.Single(meAfter!.Roles);
        Assert.Equal("Editors", meAfter.Roles[0].Name);
    }

    [Fact]
    public async Task GroupLifecycle_AddMember_ReflectsOnAuthMe()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(NoBackfill);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var created = await client.PostAsJsonAsync("/api/admin/groups",
            new { Name = "QA", Description = "quality team" });
        created.EnsureSuccessStatusCode();
        var group = await created.Content.ReadFromJsonAsync<GroupDto>();
        Assert.NotNull(group);

        var add = await client.PostAsJsonAsync(
            $"/api/admin/groups/{group!.Id}/members",
            new { UserId = AdminUserId });
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Contains(me!.Groups, g => g.Name == "QA");
    }

    [Fact]
    public async Task SuperAdminAssignment_FlagsIsSuperAdmin()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(NoBackfill);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var assigned = await client.PostAsJsonAsync(
            $"/api/admin/roles/{SystemRoles.SuperAdminId}/assignments",
            new { PrincipalKind = EntityKinds.User, PrincipalId = AdminUserId.ToString() });
        assigned.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me!.IsSuperAdmin);
    }

    // These tests verify the role/group/assignment CRUD + /api/auth/me wiring.
    // They predate authorization enforcement and run with it explicitly off so
    // appsettings.Development.json (which may flip Authorization:Enabled=true
    // in your dev environment) doesn't change their semantics.
    private static readonly Dictionary<string, string?> NoBackfill = new()
    {
        ["Authorization:Enabled"] = "false",
        ["Authorization:Enforcement"] = "off",
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private sealed record MeResponse(
        bool Authenticated,
        string? UserId,
        string? Username,
        bool IsSuperAdmin,
        IReadOnlyList<RoleDto> Roles,
        IReadOnlyList<GroupDto> Groups);

    private sealed record RoleDto(Guid Id, string Name, bool IsSystem);

    private sealed record GroupDto(Guid Id, string Name);
}
