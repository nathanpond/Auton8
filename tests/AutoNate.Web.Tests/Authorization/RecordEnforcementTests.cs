using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RecordEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JsonElement EmptyValues()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static ClaimsPrincipal Actor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task ListAuthorized_NoGrant_ReturnsEmpty()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E1", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "R1", null, null, EmptyValues(), null), AdminUserId);

        var unrelatedUser = Guid.NewGuid();
        var page = await recordStore.ListAuthorizedAsync(Actor(unrelatedUser), type.Id, page: 0, pageSize: 50, includeArchived: false);

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Records);
    }

    [Fact]
    public async Task ListAuthorized_SuperAdmin_SeesEverything()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var assignments = db.CreateRoleAssignmentStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E2", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "A", null, null, EmptyValues(), null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "B", null, null, EmptyValues(), null), AdminUserId);

        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            SystemRoles.SuperAdminId, EntityKinds.User, AdminUserId.ToString(), null), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(AdminUserId), type.Id, 0, 50, false);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ListAuthorized_AssigneeUserGrant_LimitsToAssignedRecords()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();
        var assignments = db.CreateRoleAssignmentStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E3", "type", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        var assignedToAlice = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Alice's record", null, null, EmptyValues(), new[] { alice }), AdminUserId);
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Bob's record", null, null, EmptyValues(), new[] { bob }), AdminUserId);

        var role = await roles.CreateAsync(new CreateRoleInput("AssigneeViewer", null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*[assignee=user]", "allow", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.User, alice.ToString(), null), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(alice), type.Id, 0, 50, false);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(assignedToAlice.Id, page.Records[0].Id);
    }

    [Fact]
    public async Task ListAuthorized_WildcardGrant_SeesAllRecords()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();
        var assignments = db.CreateRoleAssignmentStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E4", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "R1", null, null, EmptyValues(), null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "R2", null, null, EmptyValues(), null), AdminUserId);

        var alice = Guid.NewGuid();
        var role = await roles.CreateAsync(new CreateRoleInput("ReadAll", null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*", "allow", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.User, alice.ToString(), null), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(alice), type.Id, 0, 50, false);

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ListAuthorized_DenyOverridesAllow()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();
        var assignments = db.CreateRoleAssignmentStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E5", "type", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "Alice's", null, null, EmptyValues(), new[] { alice }), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "Other", null, null, EmptyValues(), null), AdminUserId);

        var role = await roles.CreateAsync(new CreateRoleInput("ReadAllExceptAssignedToMe", null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*", "allow", 0), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*[assignee=user]", "deny", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.User, alice.ToString(), null), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(alice), type.Id, 0, 50, false);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Other", page.Records[0].Name);
    }

    [Fact]
    public async Task ListAuthorized_PaginationCount_ReflectsFilteredSet()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();
        var assignments = db.CreateRoleAssignmentStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E6", "type", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await recordStore.CreateAsync(new CreateRecordInput(type.Id, $"A{i}", null, null, EmptyValues(),
                new[] { alice }), AdminUserId);
        }
        for (var i = 0; i < 7; i++)
        {
            await recordStore.CreateAsync(new CreateRecordInput(type.Id, $"X{i}", null, null, EmptyValues(), null), AdminUserId);
        }

        var role = await roles.CreateAsync(new CreateRoleInput("AssignedOnly", null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*[assignee=user]", "allow", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.User, alice.ToString(), null), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(alice), type.Id, 0, 3, false);

        Assert.Equal(5, page.TotalCount);          // total filtered
        Assert.Equal(3, page.Records.Count);       // page size
    }

    [Fact]
    public async Task ListAuthorized_GroupGrant_GrantsThroughMembership()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();
        var groups = db.CreateGroupStore();
        var assignments = db.CreateRoleAssignmentStore();

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E7", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "R", null, null, EmptyValues(), null), AdminUserId);

        var alice = Guid.NewGuid();
        var group = await groups.CreateAsync(new CreateGroupInput("Readers", null), AdminUserId);
        await groups.AddMemberAsync(group.Id, alice, AdminUserId);

        var role = await roles.CreateAsync(new CreateRoleInput("ViaGroup", null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*", "allow", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.Group, group.Id.ToString(), null), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(alice), type.Id, 0, 50, false);

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ListAuthorized_WhenDisabled_ReturnsAllRegardless()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: false);

        var type = await typeStore.CreateAsync(new CreateRecordTypeInput("E8", "type", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "R1", null, null, EmptyValues(), null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(type.Id, "R2", null, null, EmptyValues(), null), AdminUserId);

        var anonymousUser = Guid.NewGuid();
        var page = await recordStore.ListAuthorizedAsync(Actor(anonymousUser), type.Id, 0, 50, false);

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ListAuthorized_RecordTypeTag_FiltersByShortCode()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var roles = db.CreateRoleStore();
        var grants = db.CreatePermissionGrantStore();
        var assignments = db.CreateRoleAssignmentStore();

        var leads = await typeStore.CreateAsync(new CreateRecordTypeInput("LEAD", "leads", null, null, null), AdminUserId);
        var deals = await typeStore.CreateAsync(new CreateRecordTypeInput("DEAL", "deals", null, null, null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(leads.Id, "L1", null, null, EmptyValues(), null), AdminUserId);
        await recordStore.CreateAsync(new CreateRecordInput(deals.Id, "D1", null, null, EmptyValues(), null), AdminUserId);

        var alice = Guid.NewGuid();
        var role = await roles.CreateAsync(new CreateRoleInput("LeadsOnly", null), AdminUserId);
        await grants.CreateAsync(new CreatePermissionGrantInput(EntityKinds.Role, role.Id.ToString(), Actions.View, "/record/*[recordtype=LEAD]", "allow", 0), AdminUserId);
        await assignments.AssignAsync(new CreateRoleAssignmentInput(
            role.Id, EntityKinds.User, alice.ToString(), null), AdminUserId);

        // Listing without recordTypeId filter to be sure the tag predicate is what filters.
        var page = await recordStore.ListAuthorizedAsync(Actor(alice), null, 0, 50, false);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(leads.Id, page.Records[0].RecordTypeId);
    }
}
