using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class ExplainTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task SuperAdmin_ShortCircuits_AllowWithNoGrants()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var assignments = db.CreateRoleAssignmentStore();
        var authorizer = db.CreateAuthorizer(enabled: true);

        var user = Guid.NewGuid();
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            SystemRoles.SuperAdminId, EntityKinds.User, user.ToString(), null), AdminUserId);

        var result = await authorizer.ExplainAsync(
            user, Actions.View, new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.Equal(AuthEffect.Allow, result.Effect);
        Assert.True(result.IsSuperAdmin);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public async Task NoGrants_ReturnsDeny_WithEmptyTrace()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var authorizer = db.CreateAuthorizer(enabled: true);

        var user = Guid.NewGuid();
        var result = await authorizer.ExplainAsync(
            user, Actions.View, new EntityRef(EntityKinds.Record, Guid.NewGuid().ToString()));

        Assert.Equal(AuthEffect.Deny, result.Effect);
        Assert.False(result.IsSuperAdmin);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public async Task AssigneeGrant_MatchesAssignedRecord_ButNotOthers()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();
        var authorizer = db.CreateAuthorizer(enabled: true);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("EX", "ex", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        var assigned = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "for-alice", null, null, Empty(), new[] { alice }), AdminUserId);
        var unassigned = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "no-one", null, null, Empty(), null), AdminUserId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*[assignee=user]", "allow", 0), AdminUserId);

        var hit = await authorizer.ExplainAsync(
            alice, Actions.View, new EntityRef(EntityKinds.Record, assigned.Id.ToString()));
        Assert.Equal(AuthEffect.Allow, hit.Effect);
        Assert.Single(hit.Grants);
        Assert.True(hit.Grants[0].Matched);

        var miss = await authorizer.ExplainAsync(
            alice, Actions.View, new EntityRef(EntityKinds.Record, unassigned.Id.ToString()));
        Assert.Equal(AuthEffect.Deny, miss.Effect);
        Assert.Single(miss.Grants);
        Assert.False(miss.Grants[0].Matched);
    }

    [Fact]
    public async Task MultiHopGrant_Matches_RecordAssignedToSupervisee()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();
        var authorizer = db.CreateAuthorizer(enabled: true);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("EX2", "ex2", null, null, null), AdminUserId);
        var carol = Guid.NewGuid();
        var alice = Guid.NewGuid();

        // Carol supervises Alice.
        await using (var ctx = db.CreateDbContext())
        {
            ctx.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
            {
                Id = Guid.NewGuid(),
                EdgeKind = EdgeKinds.Supervisor,
                FromKind = EntityKinds.User,
                FromId = carol.ToString(),
                ToKind = EntityKinds.User,
                ToId = alice.ToString(),
                Data = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = AdminUserId
            });
            await ctx.SaveChangesAsync();
        }

        var record = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "for-alice", null, null, Empty(), new[] { alice }), AdminUserId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, carol.ToString(),
            Actions.View, "/record/*[assignee=user[supervisor=user]]", "allow", 0), AdminUserId);

        var result = await authorizer.ExplainAsync(
            carol, Actions.View, new EntityRef(EntityKinds.Record, record.Id.ToString()));

        Assert.Equal(AuthEffect.Allow, result.Effect);
        Assert.Single(result.Grants);
        Assert.True(result.Grants[0].Matched);
        Assert.Equal("/record/*[assignee=user[supervisor=user]]", result.Grants[0].SelectorString);
    }

    [Fact]
    public async Task DenyGrant_OverridesAllow_EvenIfBothMatch()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();
        var authorizer = db.CreateAuthorizer(enabled: true);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("EX3", "ex3", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        var record = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "x", null, null, Empty(), new[] { alice }), AdminUserId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*", "allow", 0), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/record/*[assignee=user]", "deny", 0), AdminUserId);

        var result = await authorizer.ExplainAsync(
            alice, Actions.View, new EntityRef(EntityKinds.Record, record.Id.ToString()));

        Assert.Equal(AuthEffect.Deny, result.Effect);
        Assert.Equal(2, result.Grants.Count);
        Assert.Contains(result.Grants, g => g.Effect == AuthEffect.Allow && g.Matched == true);
        Assert.Contains(result.Grants, g => g.Effect == AuthEffect.Deny && g.Matched == true);
    }

    [Fact]
    public async Task KindMismatch_GrantRecorded_AsUnmatched()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();
        var authorizer = db.CreateAuthorizer(enabled: true);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("EX4", "ex4", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        var record = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "x", null, null, Empty(), new[] { alice }), AdminUserId);

        // Grant on workflowmodel; we ask about a record. Wrong kind — recorded but unmatched.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, alice.ToString(),
            Actions.View, "/workflowmodel/*", "allow", 0), AdminUserId);

        var result = await authorizer.ExplainAsync(
            alice, Actions.View, new EntityRef(EntityKinds.Record, record.Id.ToString()));

        Assert.Equal(AuthEffect.Deny, result.Effect);
        Assert.Single(result.Grants);
        Assert.False(result.Grants[0].Matched);
    }
}
