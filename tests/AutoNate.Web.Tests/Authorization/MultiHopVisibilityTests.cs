using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class MultiHopVisibilityTests
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

    // Inserts a supervisor edge directly; the runtime path uses the API but
    // these tests bypass HTTP for clarity.
    private static async Task LinkSupervisorAsync(
        PostgresTestDatabase db, Guid supervisor, Guid supervisee)
    {
        await using var ctx = db.CreateDbContext();
        ctx.EntityEdges.Add(new AutoNate.Web.Persistence.Scaffolded.EntityEdge
        {
            Id = Guid.NewGuid(),
            EdgeKind = EdgeKinds.Supervisor,
            FromKind = EntityKinds.User,
            FromId = supervisor.ToString(),
            ToKind = EntityKinds.User,
            ToId = supervisee.ToString(),
            Data = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = AdminUserId
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Supervisor_SeesRecordsAssignedToSupervisees_Only()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("MH", "multi-hop", null, null, null), AdminUserId);

        // Hierarchy: Carol supervises Alice. Bob is unrelated.
        var carol = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await LinkSupervisorAsync(db, supervisor: carol, supervisee: alice);

        // Three records: one assigned to Alice (Carol's supervisee), one to
        // Bob (unrelated), one with no assignee.
        var aliceRec = await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Alice's record", null, null, Empty(), new[] { alice }),
            AdminUserId);
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Bob's record", null, null, Empty(), new[] { bob }),
            AdminUserId);
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "Unassigned", null, null, Empty(), null),
            AdminUserId);

        // Direct grant: Carol can view records assigned to people she supervises.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, carol.ToString(),
            Actions.View,
            "/record/*[assignee=user[supervisor=user]]",
            "allow", 0), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(carol), type.Id, 0, 50, false);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(aliceRec.Id, page.Records[0].Id);
    }

    [Fact]
    public async Task NonSupervisor_DoesNotSeeRecordsViaMultiHopGrant()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var typeStore = db.CreateRecordTypeStore();
        var recordStore = db.CreateRecordStore(authorizationEnabled: true, AuthorizationEnforcement.ReadOnly);
        var grants = db.CreatePermissionGrantStore();

        var type = await typeStore.CreateAsync(
            new CreateRecordTypeInput("NM", "no-mh", null, null, null), AdminUserId);
        var alice = Guid.NewGuid();
        await recordStore.CreateAsync(
            new CreateRecordInput(type.Id, "R", null, null, Empty(), new[] { alice }), AdminUserId);

        // Bob has the same selector but supervises no one — he sees nothing.
        var bob = Guid.NewGuid();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, bob.ToString(),
            Actions.View,
            "/record/*[assignee=user[supervisor=user]]",
            "allow", 0), AdminUserId);

        var page = await recordStore.ListAuthorizedAsync(Actor(bob), type.Id, 0, 50, false);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task SupervisorEndpoint_PutThenGet_ReturnsAssignment()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var supervisee = Guid.NewGuid();
        var supervisor = Guid.NewGuid();

        var put = await client.PutAsJsonAsync(
            $"/api/users/{supervisee}/supervisor",
            new { supervisorUserId = supervisor });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, put.StatusCode);

        var get = await client.GetFromJsonAsync<SupervisorDto>(
            $"/api/users/{supervisee}/supervisor");
        Assert.NotNull(get);
        Assert.Equal(supervisor, get!.SupervisorUserId);

        // Clearing should return null on subsequent GET.
        await client.PutAsJsonAsync(
            $"/api/users/{supervisee}/supervisor",
            new { supervisorUserId = (Guid?)null });
        var cleared = await client.GetFromJsonAsync<SupervisorDto>(
            $"/api/users/{supervisee}/supervisor");
        Assert.Null(cleared!.SupervisorUserId);
    }

    [Fact]
    public async Task SupervisorEndpoint_RejectsSelfSupervision()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var u = Guid.NewGuid();
        var resp = await client.PutAsJsonAsync(
            $"/api/users/{u}/supervisor",
            new { supervisorUserId = u });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private sealed record SupervisorDto(Guid UserId, Guid? SupervisorUserId);
}
