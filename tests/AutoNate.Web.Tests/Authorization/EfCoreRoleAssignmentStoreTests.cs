using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class EfCoreRoleAssignmentStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AssignAsync_ToUser_Works()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var assignments = db.CreateRoleAssignmentStore();

        var role = await roles.CreateAsync(new CreateRoleInput("Editor", null), Actor);
        var userId = Guid.NewGuid();

        var assignment = await assignments.AssignAsync(
            new CreateRoleAssignmentInput(role.Id, EntityKinds.User, userId.ToString(), null),
            Actor);

        Assert.Equal(role.Id, assignment.RoleId);
        Assert.Equal(EntityKinds.User, assignment.PrincipalKind);
        Assert.Equal(userId.ToString(), assignment.PrincipalId);
    }

    [Fact]
    public async Task AssignAsync_Duplicate_ReturnsExisting()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var assignments = db.CreateRoleAssignmentStore();
        var role = await roles.CreateAsync(new CreateRoleInput("Once", null), Actor);
        var userId = Guid.NewGuid();

        var first = await assignments.AssignAsync(
            new CreateRoleAssignmentInput(role.Id, EntityKinds.User, userId.ToString(), null),
            Actor);
        var second = await assignments.AssignAsync(
            new CreateRoleAssignmentInput(role.Id, EntityKinds.User, userId.ToString(), null),
            Actor);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await assignments.ListByRoleAsync(role.Id));
    }

    [Fact]
    public async Task AssignAsync_BadPrincipalKind_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var assignments = db.CreateRoleAssignmentStore();
        var role = await roles.CreateAsync(new CreateRoleInput("X", null), Actor);

        await Assert.ThrowsAsync<RoleAssignmentValidationException>(() =>
            assignments.AssignAsync(
                new CreateRoleAssignmentInput(role.Id, "robot", "x", null), Actor));
    }

    [Fact]
    public async Task AssignAsync_InvalidScope_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var assignments = db.CreateRoleAssignmentStore();
        var role = await roles.CreateAsync(new CreateRoleInput("Y", null), Actor);

        await Assert.ThrowsAsync<RoleAssignmentValidationException>(() =>
            assignments.AssignAsync(
                new CreateRoleAssignmentInput(role.Id, EntityKinds.User, Guid.NewGuid().ToString(),
                    "garbage scope without leading slash"),
                Actor));
    }

    [Fact]
    public async Task ListForPrincipalAsync_ReturnsBoth()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var assignments = db.CreateRoleAssignmentStore();
        var r1 = await roles.CreateAsync(new CreateRoleInput("R1", null), Actor);
        var r2 = await roles.CreateAsync(new CreateRoleInput("R2", null), Actor);
        var userId = Guid.NewGuid().ToString();
        await assignments.AssignAsync(new CreateRoleAssignmentInput(r1.Id, EntityKinds.User, userId, null), Actor);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(r2.Id, EntityKinds.User, userId, null), Actor);

        var list = await assignments.ListForPrincipalAsync(EntityKinds.User, userId);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task RevokeAsync_Removes()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var roles = db.CreateRoleStore();
        var assignments = db.CreateRoleAssignmentStore();
        var role = await roles.CreateAsync(new CreateRoleInput("Gone", null), Actor);
        var userId = Guid.NewGuid().ToString();
        var a = await assignments.AssignAsync(
            new CreateRoleAssignmentInput(role.Id, EntityKinds.User, userId, null), Actor);

        Assert.True(await assignments.RevokeAsync(a.Id));
        Assert.Empty(await assignments.ListByRoleAsync(role.Id));
    }
}
