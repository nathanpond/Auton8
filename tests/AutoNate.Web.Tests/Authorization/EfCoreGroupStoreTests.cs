using AutoNate.Web.Services.Authorization;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class EfCoreGroupStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CreateAsync_PersistsGroup()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateGroupStore();

        var group = await store.CreateAsync(new CreateGroupInput("Engineering", null), Actor);

        Assert.Equal("Engineering", group.Name);
        Assert.False(group.IsArchived);
        Assert.NotNull(await store.GetAsync(group.Id));
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateGroupStore();
        await store.CreateAsync(new CreateGroupInput("Eng", null), Actor);
        await Assert.ThrowsAsync<GroupValidationException>(() =>
            store.CreateAsync(new CreateGroupInput("Eng", null), Actor));
    }

    [Fact]
    public async Task AddMember_Idempotent()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateGroupStore();
        var group = await store.CreateAsync(new CreateGroupInput("Members", null), Actor);

        var user = Guid.NewGuid();
        Assert.True(await store.AddMemberAsync(group.Id, user, Actor));
        Assert.False(await store.AddMemberAsync(group.Id, user, Actor)); // already a member
        Assert.Single(await store.ListMembersAsync(group.Id));
    }

    [Fact]
    public async Task RemoveMember_Works()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateGroupStore();
        var group = await store.CreateAsync(new CreateGroupInput("Removers", null), Actor);
        var user = Guid.NewGuid();
        await store.AddMemberAsync(group.Id, user, Actor);

        Assert.True(await store.RemoveMemberAsync(group.Id, user));
        Assert.Empty(await store.ListMembersAsync(group.Id));
    }

    [Fact]
    public async Task ListGroupsForUser_ReturnsContainingGroups()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateGroupStore();
        var g1 = await store.CreateAsync(new CreateGroupInput("A", null), Actor);
        var g2 = await store.CreateAsync(new CreateGroupInput("B", null), Actor);
        await store.CreateAsync(new CreateGroupInput("C", null), Actor);

        var user = Guid.NewGuid();
        await store.AddMemberAsync(g1.Id, user, Actor);
        await store.AddMemberAsync(g2.Id, user, Actor);

        var groups = await store.ListGroupsForUserAsync(user);
        var names = groups.Select(g => g.Name).ToHashSet();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
        Assert.DoesNotContain("C", names);
    }

    [Fact]
    public async Task SetArchivedAsync_TogglesAndExcludesFromDefaultList()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreateGroupStore();
        var group = await store.CreateAsync(new CreateGroupInput("Hidden", null), Actor);

        await store.SetArchivedAsync(group.Id, archived: true, Actor);

        var visible = await store.ListAsync(includeArchived: false);
        Assert.DoesNotContain(visible, g => g.Id == group.Id);

        var all = await store.ListAsync(includeArchived: true);
        Assert.Contains(all, g => g.Id == group.Id);
    }
}
