using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class EfCoreRoleStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CreateAsync_PersistsRole()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateRoleStore();

        var role = await store.CreateAsync(new CreateRoleInput("Editors", "Can edit records"), Actor);

        Assert.Equal("Editors", role.Name);
        Assert.False(role.IsSystem);

        var fetched = await store.GetAsync(role.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Editors", fetched!.Name);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateRoleStore();
        await store.CreateAsync(new CreateRoleInput("Editors", null), Actor);
        await Assert.ThrowsAsync<RoleValidationException>(() =>
            store.CreateAsync(new CreateRoleInput("Editors", null), Actor));
    }

    [Fact]
    public async Task ListAsync_IncludesSeededSuperAdmin()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateRoleStore();

        var roles = await store.ListAsync();
        Assert.Contains(roles, r => r.Id == SystemRoles.SuperAdminId && r.IsSystem);
    }

    [Fact]
    public async Task DeleteAsync_SystemRole_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateRoleStore();
        await Assert.ThrowsAsync<RoleValidationException>(() =>
            store.DeleteAsync(SystemRoles.SuperAdminId));
    }

    [Fact]
    public async Task UpdateAsync_SystemRole_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateRoleStore();
        await Assert.ThrowsAsync<RoleValidationException>(() =>
            store.UpdateAsync(SystemRoles.SuperAdminId, new UpdateRoleInput("X", null), Actor));
    }

    [Fact]
    public async Task DeleteAsync_NormalRole_Removes()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateRoleStore();
        var role = await store.CreateAsync(new CreateRoleInput("Temp", null), Actor);

        var deleted = await store.DeleteAsync(role.Id);
        Assert.True(deleted);
        Assert.Null(await store.GetAsync(role.Id));
    }

    [Fact]
    public async Task DeleteAsync_CascadesPermissionGrantsForRole()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();

        var role = await roles.CreateAsync(new CreateRoleInput("WithGrants", null), Actor);
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.Role, role.Id.ToString(),
            Actions.View, "/record/*", "allow", 0), Actor);

        await roles.DeleteAsync(role.Id);

        var leftover = await grants.ListForPrincipalAsync(EntityKinds.Role, role.Id.ToString());
        Assert.Empty(leftover);
    }
}
