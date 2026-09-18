using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;

namespace AutoNate.Web.Tests.Authorization;

/// <summary>
/// An uncompilable DENY fails the request closed; an uncompilable ALLOW is
/// still skipped (#577).
/// </summary>
/// <remarks>
/// <para>
/// <c>Authorizer</c> caught <c>SelectorCompilationException</c> and skipped the
/// grant with a log warning, for allows and denies alike. Both compilers serve
/// both effects, so a skipped allow locked someone out of rows they were
/// entitled to — visible, annoying, safe — while a skipped deny silently
/// stopped denying, which is a leak nobody sees.
/// </para>
/// <para>
/// Both directions are asserted. Asserting only the deny half would pass
/// against a change that failed EVERY uncompilable grant closed, which would
/// turn a harmless allow typo into an outage.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class UncompilableGrantDirectionTests
{
    // `owner` is not a workflowmodel tag, so WorkflowModelSelectorCompiler
    // throws SelectorCompilationException on it. Chosen deliberately over a
    // malformed string: this parses cleanly and fails at COMPILE time, which is
    // the path under test. A parse failure never reaches the compiler at all.
    private const string Uncompilable = "/workflowmodel/*[nosuchtag=whatever]";

    private static ClaimsPrincipal Actor(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "test"));

    [Fact]
    public async Task An_uncompilable_deny_fails_the_request_closed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        var (leadId, dealId) = await SeedTwoAsync(db);

        var grants = db.CreatePermissionGrantStore();

        // A broad allow that really does permit both rows...
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/workflowmodel/*", "allow", 0), actorId);

        // ...and a deny that cannot compile. Before #577 this was dropped with a
        // warning and BOTH rows came back — the leak this test exists to close.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, Uncompilable, "deny", 0), actorId);

        var visible = await FilterAsync(db, actorId);

        Assert.Empty(visible);
        Assert.DoesNotContain(leadId, visible);
        Assert.DoesNotContain(dealId, visible);
    }

    [Fact]
    public async Task An_uncompilable_allow_is_still_skipped_and_the_others_still_grant()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        var (leadId, dealId) = await SeedTwoAsync(db);

        var grants = db.CreatePermissionGrantStore();

        // One allow that cannot compile — skipped, contributing nothing.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, Uncompilable, "allow", 0), actorId);

        // One that compiles and names exactly one row.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, "/workflowmodel/*[processkey=lead]", "allow", 0), actorId);

        var visible = await FilterAsync(db, actorId);

        // The complement that keeps the fix honest: the request did NOT fail
        // closed. A change that failed every uncompilable grant closed would
        // return nothing here, and the deny test above would still pass.
        Assert.Contains(leadId, visible);
        Assert.DoesNotContain(dealId, visible);
    }

    [Fact]
    public async Task An_uncompilable_allow_alone_grants_nothing_rather_than_everything()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        _ = await SeedTwoAsync(db);

        var grants = db.CreatePermissionGrantStore();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, Uncompilable, "allow", 0), actorId);

        // Skipping the only allow leaves no allows, which already closed the
        // query before #577. Pinned so the new deny branch cannot be widened
        // into "any compilation failure opens up".
        Assert.Empty(await FilterAsync(db, actorId));
    }

    private static async Task<HashSet<Guid>> FilterAsync(PostgresTestDatabase db, Guid actorId)
    {
        var authorizer = db.CreateAuthorizer(enabled: true, AuthorizationEnforcement.Full);
        await using var ctx = db.CreateDbContext();
        var filtered = await authorizer.FilterQueryAsync(
            ctx, Actor(actorId), EntityKinds.WorkflowModel, Actions.View,
            ctx.WorkflowModels.AsNoTracking().AsQueryable());
        var rows = await filtered.ToListAsync();
        return rows.Select(m => m.Id).ToHashSet();
    }

    private static async Task<(Guid lead, Guid deal)> SeedTwoAsync(PostgresTestDatabase db)
    {
        var leadId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        await using var ctx = db.CreateDbContext();
        await ctx.WorkflowModels.AddRangeAsync(
            NewModel(leadId, "Lead", "lead"),
            NewModel(dealId, "Deal", "deal"));
        await ctx.SaveChangesAsync();
        return (leadId, dealId);
    }

    private static WorkflowModelEntity NewModel(Guid id, string name, string processKey) => new()
    {
        Id = id,
        Name = name,
        ProcessKey = processKey,
        BpmnXml = "<definitions/>",
        IsDraft = false,
        DraftVersionNumber = 1,
        PublishedVersionNumber = 1,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
