using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RecordSearchEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static ClaimsPrincipal Actor(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task SearchAsync_WithoutActor_ReturnsAllRecords()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("SR", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "A", null, null, Empty(), null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "B", null, null, Empty(), null), AdminUserId);

        var page = await recordStore.SearchAsync(new RecordSearchInput(
            type.Id, null, null, false, 0, 25, null));

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithActor_NoGrant_ReturnsEmpty()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("SR2", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "A", null, null, Empty(), null), AdminUserId);

        var page = await recordStore.SearchAsync(
            new RecordSearchInput(type.Id, null, null, false, 0, 25, null),
            Actor(Guid.NewGuid()));

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithAssigneeUserGrant_ReturnsOnlyAssignedRecords()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("SR3", "type", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var aliceRec = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Alice's", null, null, Empty(), new[] { alice }), AdminUserId);
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Bob's", null, null, Empty(), new[] { bob }), AdminUserId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*[assignee=user]", "allow", 0), AdminUserId);

        var page = await recordStore.SearchAsync(
            new RecordSearchInput(type.Id, null, null, false, 0, 25, null),
            Actor(alice));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(aliceRec.Id, page.Records[0].Id);
    }

    [Fact]
    public async Task SearchAsync_DenyOverridesAllow_ViaSql()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("SR4", "type", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "Alice's", null, null, Empty(), new[] { alice }), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "Other", null, null, Empty(), null), AdminUserId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*", "allow", 0), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*[assignee=user]", "deny", 0), AdminUserId);

        var page = await recordStore.SearchAsync(
            new RecordSearchInput(type.Id, null, null, false, 0, 25, null),
            Actor(alice));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Other", page.Records[0].Name);
    }

    [Fact]
    public async Task SearchAsync_MultiHopSupervisorGrant_AllowsSuperviseesRecords()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("SR5", "type", null, null, null), AdminUserId);
        var carol = Guid.NewGuid();
        var alice = Guid.NewGuid();

        // Carol supervises Alice.
        await using (var ctx = db.CreateDbContext())
        {
            ctx.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = "supervisor",
                FromKind = "user", FromId = carol.ToString(),
                ToKind = "user", ToId = alice.ToString(),
                Data = "{}", CreatedAtUtc = DateTime.UtcNow, CreatedBy = AdminUserId
            });
            await ctx.SaveChangesAsync();
        }

        var aliceRec = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Alice's", null, null, Empty(), new[] { alice }), AdminUserId);
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Stranger's", null, null, Empty(), new[] { Guid.NewGuid() }), AdminUserId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, carol.ToString(),
            Actions.View, "/record/*[assignee=user[supervisor=user]]", "allow", 0), AdminUserId);

        var page = await recordStore.SearchAsync(
            new RecordSearchInput(type.Id, null, null, false, 0, 25, null),
            Actor(carol));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(aliceRec.Id, page.Records[0].Id);
    }

    [Fact]
    public async Task SearchAssignedAsync_GrantRequired_BeyondAssigneeFilter()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("SR6", "type", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "A", null, null, Empty(), new[] { alice }), AdminUserId);

        // No grant -> closed default -> 0 even though alice is the assignee.
        var page = await recordStore.SearchAssignedAsync(
            alice, 0, 25, false, null, Actor(alice));
        Assert.Equal(0, page.TotalCount);
    }
}
