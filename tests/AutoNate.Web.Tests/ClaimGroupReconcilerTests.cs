using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Claim-driven group membership: granting, revoking, and what it must not touch.
/// </summary>
/// <remarks>
/// #92 exists because after #90 a federated user signs in with nothing. The
/// dangerous half is not the granting — a first implementation naturally adds
/// and it is obvious when it does not. It is the revoking: a reconciler that
/// only ever adds looks entirely correct until the day somebody leaves, and then
/// the access they should have lost is still there and nothing reported it.
///
/// So the tests here are weighted towards what must *stop* happening: a claim
/// disappearing removes the group, a manual grant survives that, one provider
/// cannot revoke another's, and nothing writes a role assignment.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ClaimGroupReconcilerTests
{
    private const string ClaimType = "groups";

    private sealed record Harness(
        AutoNateWebApplicationFactory App,
        IClaimGroupReconciler Reconciler,
        IGroupStore Groups,
        IIdentityProviderStore Providers,
        IDbContextFactory<AutoNateDbContext> Db) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static async Task<Harness> BuildAsync(
        IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var app = await AutoNateWebApplicationFactory.CreateAsync(extraConfig);
        _ = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        return new Harness(
            app,
            scope.GetRequiredService<IClaimGroupReconciler>(),
            scope.GetRequiredService<IGroupStore>(),
            scope.GetRequiredService<IIdentityProviderStore>(),
            app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>());
    }

    private static async Task<Guid> ProviderAsync(Harness h, string slug = "corp")
    {
        var provider = await h.Providers.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Oidc,
            DisplayName: slug,
            Slug: slug,
            IsEnabled: true,
            OidcAuthority: "https://idp.example.com",
            OidcClientId: "auton8",
            OidcScopes: null,
            SamlEntityId: null, SamlMetadataUrl: null, SamlMetadataXml: null,
            SamlSigningCertificate: null,
            Secret: null), Guid.NewGuid(), CancellationToken.None);
        return provider.Id;
    }

    private static async Task MapAsync(Harness h, Guid providerId, string claimValue, Guid groupId)
    {
        await using var db = await h.Db.CreateDbContextAsync();
        db.IdentityProviderGroupMappings.Add(new IdentityProviderGroupMappingModel
        {
            Id = Guid.NewGuid(),
            ProviderId = providerId,
            ClaimType = ClaimType,
            ClaimValue = claimValue,
            GroupId = groupId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid(),
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static Dictionary<string, string[]> Claims(params string[] values) =>
        new(StringComparer.Ordinal) { [ClaimType] = values };

    private static async Task<List<Guid>> MembershipsAsync(Harness h, Guid userId)
    {
        await using var db = await h.Db.CreateDbContextAsync();
        return await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
    }

    // ── Granting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_mapped_claim_grants_the_group()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", group.Id);

        var user = Guid.NewGuid();
        var result = await h.Reconciler.ReconcileAsync(
            user, provider, Claims("engineering"), CancellationToken.None);

        Assert.Equal([group.Id], result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal([group.Id], await MembershipsAsync(h, user));
    }

    [Fact]
    public async Task A_granted_group_carries_the_permissions_it_holds()
    {
        // Asserted through an actual authorization decision rather than by
        // reading the membership row. The membership existing is not the point;
        // the point is that the person can now do something they could not do
        // before, and only the authorizer can say whether that is true.
        await using var h = await BuildAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = "full",
        });

        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", group.Id);

        var scope = h.App.Services.CreateScope().ServiceProvider;
        var grants = scope.GetRequiredService<IPermissionGrantStore>();

        // The permission is held by the *group*, not the user. That is the
        // whole point of mapping to a group rather than granting directly:
        // access follows membership, and membership is what a claim changes.
        var actorId = Guid.NewGuid();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.Group, group.Id.ToString(),
            Actions.View, "/group/*", "allow", 0), actorId);

        var user = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.ToString())], "test"));

        // A fresh scope per decision, because Authorizer memoises the actor's
        // groups and roles for the lifetime of one — which is right in
        // production, where a scope is a request, and would quietly make this
        // test assert against a snapshot taken before the reconciliation.
        Assert.False(await IsAllowedAsync(h, principal));

        await h.Reconciler.ReconcileAsync(user, provider, Claims("engineering"), CancellationToken.None);

        Assert.True(await IsAllowedAsync(h, principal));
    }

    private static async Task<bool> IsAllowedAsync(Harness h, ClaimsPrincipal principal)
    {
        await using var scope = h.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthorizer>()
            .IsAuthorizedAsync(principal, EntityKinds.Group, Actions.View, _ => true);
    }

    [Fact]
    public async Task An_unmapped_claim_grants_nothing_and_is_not_an_error()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", group.Id);

        // The ordinary case: an IdP hands over every group a person belongs to
        // and most of them mean nothing here. Silence is the correct response,
        // not an error an administrator would have to suppress.
        var user = Guid.NewGuid();
        var result = await h.Reconciler.ReconcileAsync(
            user, provider, Claims("sales", "everyone", "building-access"), CancellationToken.None);

        Assert.False(result.ChangedAnything);
        Assert.Empty(await MembershipsAsync(h, user));
    }

    // ── Revoking — the half most likely to be missed ────────────────────────

    [Fact]
    public async Task A_claim_that_disappears_removes_the_group()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", group.Id);

        var user = Guid.NewGuid();
        await h.Reconciler.ReconcileAsync(user, provider, Claims("engineering"), CancellationToken.None);
        Assert.Equal([group.Id], await MembershipsAsync(h, user));

        // They were removed from the group at the IdP. This is the whole reason
        // reconciliation runs on every sign-in rather than only the first.
        var result = await h.Reconciler.ReconcileAsync(
            user, provider, Claims("sales"), CancellationToken.None);

        Assert.Equal([group.Id], result.Removed);
        Assert.Empty(await MembershipsAsync(h, user));
    }

    [Fact]
    public async Task A_manual_membership_survives_a_revocation()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var fromIdp = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        var byHand = await h.Groups.CreateAsync(new CreateGroupInput("On Call", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", fromIdp.Id);

        var user = Guid.NewGuid();
        await h.Groups.AddMemberAsync(byHand.Id, user, Guid.NewGuid());
        await h.Reconciler.ReconcileAsync(user, provider, Claims("engineering"), CancellationToken.None);

        Assert.Equal(2, (await MembershipsAsync(h, user)).Count);

        // Every claim gone. The IdP-derived membership goes with it; the one an
        // administrator granted does not, because it was never the IdP's to take.
        var result = await h.Reconciler.ReconcileAsync(
            user, provider, Claims(), CancellationToken.None);

        Assert.Equal([fromIdp.Id], result.Removed);
        Assert.Equal([byHand.Id], await MembershipsAsync(h, user));
    }

    [Fact]
    public async Task One_provider_cannot_revoke_another_providers_grants()
    {
        await using var h = await BuildAsync();
        var corp = await ProviderAsync(h, "corp");
        var partner = await ProviderAsync(h, "partner");

        var corpGroup = await h.Groups.CreateAsync(new CreateGroupInput("Staff", null), Guid.NewGuid());
        var partnerGroup = await h.Groups.CreateAsync(new CreateGroupInput("Partners", null), Guid.NewGuid());
        await MapAsync(h, corp, "staff", corpGroup.Id);
        await MapAsync(h, partner, "partners", partnerGroup.Id);

        var user = Guid.NewGuid();
        await h.Reconciler.ReconcileAsync(user, corp, Claims("staff"), CancellationToken.None);
        await h.Reconciler.ReconcileAsync(user, partner, Claims("partners"), CancellationToken.None);

        Assert.Equal(2, (await MembershipsAsync(h, user)).Count);

        // Signing in through one provider must not reconcile away the other's
        // grants. Without the provider id on the membership row this is exactly
        // what would happen, and it would look like a random loss of access.
        var result = await h.Reconciler.ReconcileAsync(
            user, corp, Claims("staff"), CancellationToken.None);

        Assert.False(result.ChangedAnything);
        Assert.Equal(2, (await MembershipsAsync(h, user)).Count);
    }

    [Fact]
    public async Task A_manual_add_takes_ownership_of_an_idp_derived_row()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", group.Id);

        var user = Guid.NewGuid();
        await h.Reconciler.ReconcileAsync(user, provider, Claims("engineering"), CancellationToken.None);

        // An administrator sees the membership and grants it deliberately. If
        // this reported "already a member" and changed nothing, they would
        // reasonably believe the access was now theirs to keep — and watch it
        // vanish at the user's next sign-in, because the row was never theirs.
        Assert.True(await h.Groups.AddMemberAsync(group.Id, user, Guid.NewGuid()));

        var result = await h.Reconciler.ReconcileAsync(
            user, provider, Claims(), CancellationToken.None);

        Assert.False(result.ChangedAnything);
        Assert.Equal([group.Id], await MembershipsAsync(h, user));
    }

    [Fact]
    public async Task A_repeat_manual_add_still_reports_no_change()
    {
        await using var h = await BuildAsync();
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        var user = Guid.NewGuid();

        Assert.True(await h.Groups.AddMemberAsync(group.Id, user, Guid.NewGuid()));
        Assert.False(await h.Groups.AddMemberAsync(group.Id, user, Guid.NewGuid()));
    }

    // ── What reconciliation must never do ───────────────────────────────────

    [Fact]
    public async Task Reconciliation_never_writes_a_role_assignment()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        await MapAsync(h, provider, "engineering", group.Id);

        var user = Guid.NewGuid();
        await h.Reconciler.ReconcileAsync(user, provider, Claims("engineering"), CancellationToken.None);

        // The epic's concern, guarded directly: federation must not become a
        // second bulk-grant path. Mapping grants group membership and nothing
        // else, so the group → role path stays the single place authorization is
        // reasoned about.
        await using var db = await h.Db.CreateDbContextAsync();
        var userAssignments = await db.RoleAssignments
            .CountAsync(a => a.PrincipalId == user.ToString());
        Assert.Equal(0, userAssignments);

        var groupAssignments = await db.RoleAssignments
            .CountAsync(a => a.PrincipalId == group.Id.ToString());
        Assert.Equal(0, groupAssignments);
    }

    [Fact]
    public async Task An_archived_group_is_not_granted_afresh()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var group = await h.Groups.CreateAsync(new CreateGroupInput("Retired", null), Guid.NewGuid());
        await MapAsync(h, provider, "retired", group.Id);
        await h.Groups.SetArchivedAsync(group.Id, archived: true, Guid.NewGuid());

        var user = Guid.NewGuid();
        var result = await h.Reconciler.ReconcileAsync(
            user, provider, Claims("retired"), CancellationToken.None);

        // Archiving a group is a decision to stop using it. Handing out fresh
        // membership of one would quietly undo that decision on every sign-in.
        Assert.False(result.ChangedAnything);
        Assert.Empty(await MembershipsAsync(h, user));
    }

    [Fact]
    public async Task A_failed_reconciliation_leaves_memberships_as_they_were()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var keep = await h.Groups.CreateAsync(new CreateGroupInput("Keep", null), Guid.NewGuid());
        await MapAsync(h, provider, "keep", keep.Id);
        var user = Guid.NewGuid();
        await h.Reconciler.ReconcileAsync(user, provider, Claims("keep"), CancellationToken.None);
        Assert.Equal([keep.Id], await MembershipsAsync(h, user));

        // Force a failure part-way through. A trigger that refuses this user's
        // inserts is the cheapest honest way to get one: the reconciliation
        // stages the removal of Keep and the insert of Grant, and the insert is
        // what blows up — so a non-transactional implementation would leave the
        // user stripped of Keep and granted nothing, which is a silent partial
        // loss of access.
        var grant = await h.Groups.CreateAsync(new CreateGroupInput("Grant", null), Guid.NewGuid());
        await MapAsync(h, provider, "grant", grant.Id);

        await using (var db = await h.Db.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                $"""
                CREATE OR REPLACE FUNCTION refuse_test_member() RETURNS trigger AS $fn$
                BEGIN
                    IF NEW.user_id = '{user}'::uuid THEN
                        RAISE EXCEPTION 'refused by test trigger';
                    END IF;
                    RETURN NEW;
                END $fn$ LANGUAGE plpgsql;

                CREATE TRIGGER refuse_test_member_trg
                    BEFORE INSERT ON group_members
                    FOR EACH ROW EXECUTE FUNCTION refuse_test_member();
                """);
        }

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                h.Reconciler.ReconcileAsync(user, provider, Claims("grant"), CancellationToken.None));
        }
        finally
        {
            await using var db = await h.Db.CreateDbContextAsync();
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS refuse_test_member_trg ON group_members;");
        }

        // Unchanged, not half-applied.
        Assert.Equal([keep.Id], await MembershipsAsync(h, user));
    }

    // ── The preview cannot drift ────────────────────────────────────────────

    [Fact]
    public async Task The_preview_matches_what_a_sign_in_actually_grants()
    {
        await using var h = await BuildAsync();
        var provider = await ProviderAsync(h);
        var engineering = await h.Groups.CreateAsync(new CreateGroupInput("Engineering", null), Guid.NewGuid());
        var oncall = await h.Groups.CreateAsync(new CreateGroupInput("On Call", null), Guid.NewGuid());
        await h.Groups.CreateAsync(new CreateGroupInput("Sales", null), Guid.NewGuid());

        await MapAsync(h, provider, "engineering", engineering.Id);
        await MapAsync(h, provider, "engineering", oncall.Id);
        await MapAsync(h, provider, "finance", oncall.Id);

        var claims = Claims("engineering", "unmapped-group");

        // One test driving both paths, per the test plan. The preview is the
        // only way to check a mapping without asking a user to sign in
        // repeatedly, so a preview that can be wrong is worse than none — it
        // would be trusted.
        var previewed = (await h.Reconciler.PreviewAsync(provider, claims, CancellationToken.None))
            .Order().ToList();

        var user = Guid.NewGuid();
        await h.Reconciler.ReconcileAsync(user, provider, claims, CancellationToken.None);
        var actual = (await MembershipsAsync(h, user)).Order().ToList();

        Assert.Equal(actual, previewed);
        Assert.Equal(new[] { engineering.Id, oncall.Id }.Order().ToList(), actual);
    }

    [Fact]
    public void The_preview_computation_is_the_only_copy_of_the_rule()
    {
        // Both paths call ComputeDesiredGroups. Asserting on it directly here
        // keeps its semantics pinned even if both callers were changed together.
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var mappings = new[]
        {
            new IdentityProviderGroupMappingModel { ClaimType = "groups", ClaimValue = "eng", GroupId = groupA },
            new IdentityProviderGroupMappingModel { ClaimType = "groups", ClaimValue = "ENG", GroupId = groupB },
        };

        var granted = ClaimGroupReconciler.ComputeDesiredGroups(
            mappings, new Dictionary<string, string[]> { ["groups"] = ["eng"] });

        // Case-sensitive, ordinal. A claim value is an identifier minted by
        // another system; letting locale casing rules decide who gets in is how
        // an install behaves differently depending on the server's culture.
        Assert.Equal([groupA], granted);
    }

    [Fact]
    public void A_claim_type_that_is_absent_entirely_grants_nothing()
    {
        var mappings = new[]
        {
            new IdentityProviderGroupMappingModel
            {
                ClaimType = "groups", ClaimValue = "eng", GroupId = Guid.NewGuid(),
            },
        };

        Assert.Empty(ClaimGroupReconciler.ComputeDesiredGroups(
            mappings, new Dictionary<string, string[]> { ["roles"] = ["eng"] }));
    }
}
