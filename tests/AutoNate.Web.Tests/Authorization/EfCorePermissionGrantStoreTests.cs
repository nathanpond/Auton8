using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class EfCorePermissionGrantStoreTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CreateAsync_ValidUserGrant_Persists()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();

        var grant = await store.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, Guid.NewGuid().ToString(),
            Actions.View, "/record/*", "allow", 0), Actor);

        Assert.Equal("user", grant.PrincipalKind);
        Assert.Equal("/record/*", grant.SelectorString);

        var all = await store.ListAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task CreateAsync_GroupGrant_Persists()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();

        var grant = await store.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.Group, Guid.NewGuid().ToString(),
            Actions.Edit, "/record/*", "deny", 5), Actor);

        Assert.Equal("group", grant.PrincipalKind);
        Assert.Equal("deny", grant.Effect);
        Assert.Equal(5, grant.Priority);
    }

    [Fact]
    public async Task CreateAsync_BadPrincipalKind_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();
        await Assert.ThrowsAsync<PermissionGrantValidationException>(() =>
            store.CreateAsync(new CreatePermissionGrantInput(
                "robot", "x", "view", "/record/*", "allow", 0), Actor));
    }

    [Fact]
    public async Task CreateAsync_BadEffect_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();
        await Assert.ThrowsAsync<PermissionGrantValidationException>(() =>
            store.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, Guid.NewGuid().ToString(),
                "view", "/record/*", "maybe", 0), Actor));
    }

    [Fact]
    public async Task CreateAsync_InvalidSelector_Throws()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();
        await Assert.ThrowsAsync<PermissionGrantValidationException>(() =>
            store.CreateAsync(new CreatePermissionGrantInput(
                EntityKinds.User, Guid.NewGuid().ToString(),
                "view", "garbage selector", "allow", 0), Actor));
    }

    [Fact]
    public async Task ListForPrincipal_ReturnsOnlyMatching()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();
        var alice = Guid.NewGuid().ToString();
        var bob = Guid.NewGuid().ToString();

        await store.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice, "view", "/record/*", "allow", 0), Actor);
        await store.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, bob, "edit", "/record/*", "allow", 0), Actor);

        var aliceGrants = await store.ListForPrincipalAsync(EntityKinds.User, alice);
        Assert.Single(aliceGrants);
        Assert.Equal("view", aliceGrants[0].Action);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var store = db.CreatePermissionGrantStore();
        var g = await store.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, Guid.NewGuid().ToString(),
            "view", "/record/*", "allow", 0), Actor);

        Assert.True(await store.DeleteAsync(g.Id));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task DirectGrant_AuthorizesRecordView_WithoutAnyRole()
    {
        // Engine-level proof: a direct grant for a user authorizes that user to
        // see records, with no role intermediary involved.
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("DG", "direct grants", null, null, null), Actor);
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "R1", null, null, Empty(), null), Actor);

        var alice = Guid.NewGuid();
        var aliceActor = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, alice.ToString()) },
                authenticationType: "test"));

        // Without a grant, Alice sees nothing.
        var emptyPage = await recordStore.ListAuthorizedAsync(aliceActor, type.Id, 0, 50, false);
        Assert.Equal(0, emptyPage.TotalCount);

        // Add a direct grant.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*", "allow", 0), Actor);

        // Re-resolve the record store so the second list call runs against a
        // fresh IAuthorizer. In production the authorizer is scoped per
        // request — grant writes in one request are picked up by subsequent
        // requests, not by an already-resolved instance whose per-request
        // grant cache predates the write.
        var refreshedStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var page = await refreshedStore.ListAuthorizedAsync(aliceActor, type.Id, 0, 50, false);
        Assert.Equal(1, page.TotalCount);
    }
}
