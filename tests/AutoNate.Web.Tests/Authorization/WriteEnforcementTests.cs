using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class WriteEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Enforcement_Off_AllowsAllRequests()
    {
        // Default factory: Authorization disabled. Existing admin path stays open.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var resp = await client.PostAsJsonAsync("/api/admin/roles", new { Name = "Editors" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Enforcement_Full_NonSuperAdmin_Gets403_OnProtectedEndpoint()
    {
        // Disable the SuperAdmin backfill so the seeded admin has no privileges.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
        });
        var client = factory.CreateClient();

        // Prime auto-login so the cookie exists.
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/admin/roles", new { Name = "Editors" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Enforcement_Full_SuperAdmin_GetsThrough()
    {
        // Backfill is opt-in: we explicitly enable it so the seeded admin
        // becomes SuperAdmin and the request passes.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
        });
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/admin/roles", new { Name = "Editors" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Enforcement_Full_DryRun_AllowsButLogs()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false",
            ["Authorization:DryRun"] = "true"
        });
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        // Without SuperAdmin and without grants this would be 403, but DryRun
        // overrides the deny back to allow.
        var resp = await client.PostAsJsonAsync("/api/admin/roles", new { Name = "DryRunRole" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Enforcement_ReadOnly_LeavesWritesAlone()
    {
        // Read-only filtering is on, but writes pass.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.ReadOnly,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
        });
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/admin/roles", new { Name = "RoVarRole" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task SuperAdminBackfill_AssignsExistingUsersOnFirstRun()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
        });
        // Booting the host runs DatabaseSchemaInitializer.EnsureAsync, which
        // includes the backfill when the option is on.
        _ = factory.CreateClient();

        await using var db = factory.Database.CreateDbContext();
        var assigned = await db.RoleAssignments.AsNoTracking()
            .AnyAsync(a => a.RoleId == SystemRoles.SuperAdminId
                        && a.PrincipalKind == EntityKinds.User
                        && a.PrincipalId == AdminUserId.ToString());
        Assert.True(assigned);

        var stateExists = await db.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM auth_seed_state WHERE key = 'superadmin_backfill_v1'")
            .CountAsync();
        Assert.Equal(1, stateExists);
    }

    [Fact]
    public async Task SuperAdminBackfill_DisabledOption_DoesNotAssign()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
        });
        _ = factory.CreateClient();

        await using var db = factory.Database.CreateDbContext();
        var assigned = await db.RoleAssignments.AsNoTracking()
            .AnyAsync(a => a.RoleId == SystemRoles.SuperAdminId
                        && a.PrincipalKind == EntityKinds.User
                        && a.PrincipalId == AdminUserId.ToString());
        Assert.False(assigned);
    }
}
