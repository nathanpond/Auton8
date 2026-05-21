using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// End-to-end tests for the ContentAuthorizer.GetAllowedIdsAsync policy. The
// matrix below targets the slow-path SQL specifically — every test creates
// fresh entities under fresh Guids so they don't collide with siblings, then
// asserts the access set against a hand-computed expectation. The same matrix
// is the regression guard for the closest-depth-deny-wins tiebreak rule that
// previously lived in the per-resource C# loop.
[Trait("Category", "Integration")]
public sealed class ContentAuthorizerPolicyTests
{
    [Fact]
    public async Task NoGrants_ReturnsEmpty()
    {
        await using var harness = await Harness.CreateAsync();
        _ = await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.Empty(result.AllowedIds);
    }

    [Fact]
    public async Task SuperAdmin_ReturnsUnrestricted()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.MakeSuperAdminAsync(actor);

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.True(result.Unrestricted);
    }

    [Fact]
    public async Task WildcardAllowNoDeny_ReturnsUnrestricted()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, "/*", "allow");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.True(result.Unrestricted);
    }

    [Fact]
    public async Task ContributorMembership_FastPathSeesProjectPagesOnly()
    {
        await using var harness = await Harness.CreateAsync();
        var (projectA, _, _, pageA) = await harness.SeedTreeAsync();
        var (_, _, _, pageB) = await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(projectA, actor, ProjectRoleNames.Contributor);

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.Contains(pageA, result.AllowedIds);
        Assert.DoesNotContain(pageB, result.AllowedIds);
    }

    [Fact]
    public async Task WildcardAllowWithCarveOutDeny_AllowsEverythingExceptDeniedPage()
    {
        await using var harness = await Harness.CreateAsync();
        var (_, _, _, pageA1) = await harness.SeedTreeAsync(extraPages: 1);
        var allPagesA = harness.LastPages.ToArray(); // pageA1 + 1 extra
        var (_, _, _, pageB1) = await harness.SeedTreeAsync(extraPages: 1);
        var allPagesB = harness.LastPages.ToArray();

        var deniedPage = allPagesA[1]; // an "extra" page; canary that other pages are still allowed

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, "/*", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{deniedPage}", "deny");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.DoesNotContain(deniedPage, result.AllowedIds);
        Assert.Contains(pageA1, result.AllowedIds);
        Assert.Contains(pageB1, result.AllowedIds);
        foreach (var p in allPagesB) Assert.Contains(p, result.AllowedIds);
    }

    [Fact]
    public async Task ExplicitOverrideAllow_ReachesPageOutsideMembership()
    {
        // Actor has no membership at all. A direct page-level allow grant
        // should still surface that page in the access set — this is the
        // share-link scenario where the SQL fast path can't help (no
        // membership) and the slow path's override-allow CTE adds reach.
        await using var harness = await Harness.CreateAsync();
        var (_, _, _, pageA) = await harness.SeedTreeAsync();
        var (_, _, _, pageB) = await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageB}", "allow");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.Equal(new[] { pageB }, result.AllowedIds.OrderBy(x => x).ToArray());
        Assert.DoesNotContain(pageA, result.AllowedIds);
    }

    [Fact]
    public async Task PageLevelDenyBeatsCabinetLevelAllow_ClosestDepthWins()
    {
        // Cabinet-level allow reaches every descendant page at depth ≥ 1.
        // A page-level deny on one of those pages sits at depth 0 — the
        // closer override. Deny at the closer depth wins for that page;
        // its sibling stays allowed.
        await using var harness = await Harness.CreateAsync();
        var (_, cabinet, _, pageA) = await harness.SeedTreeAsync(extraPages: 1);
        var siblingPage = harness.LastPages[1];

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/cabinet/{cabinet}", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageA}", "deny");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.DoesNotContain(pageA, result.AllowedIds);
        Assert.Contains(siblingPage, result.AllowedIds);
    }

    [Fact]
    public async Task PageLevelAllowBeatsCabinetLevelDeny_ClosestDepthWins()
    {
        // Mirror of the previous test. Cabinet-level deny vs page-level
        // allow: the more-specific allow wins for that page; its sibling
        // (covered only by the cabinet deny) stays excluded.
        await using var harness = await Harness.CreateAsync();
        var (_, cabinet, _, pageA) = await harness.SeedTreeAsync(extraPages: 1);
        var siblingPage = harness.LastPages[1];

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/cabinet/{cabinet}", "deny");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageA}", "allow");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.Contains(pageA, result.AllowedIds);
        Assert.DoesNotContain(siblingPage, result.AllowedIds);
    }

    [Fact]
    public async Task SameDepthDenyBeatsAllow()
    {
        // Two grants on the same target at the same depth — one allow, one
        // deny — must resolve to deny. The real-world shape is usually a
        // user-allow + group-deny; the resolution logic is identical regardless
        // of which principal carries each effect, and using two user grants
        // here keeps the test free of group-membership setup.
        await using var harness = await Harness.CreateAsync();
        var (_, _, _, pageA) = await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageA}", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageA}", "deny");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.DoesNotContain(pageA, result.AllowedIds);
    }

    [Fact]
    public async Task DeleteLock_ExcludesEvenOverrideAllowedResources()
    {
        // Project deletions-lock toggle blocks Delete on every resource
        // inside, regardless of how the actor reached the resource (role
        // baseline, wildcard, or explicit override allow). The same lock
        // must NOT affect View.
        await using var harness = await Harness.CreateAsync();
        var (_, _, _, pageA) = await harness.SeedTreeAsync(deletionsLocked: true);

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.Delete, $"/page/{pageA}", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageA}", "allow");

        var deletable = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.Delete, default);
        var viewable = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.DoesNotContain(pageA, deletable.AllowedIds);
        Assert.Contains(pageA, viewable.AllowedIds);
    }

    [Fact]
    public async Task NestedPage_ChildPageDenyAtDepthZeroBeatsParentPageAllowAtDepthOne()
    {
        // Closure: child-page → parent-page → notebook → cabinet → project.
        // An allow on the parent reaches the child at depth 1; a deny on
        // the child sits at depth 0 of the child's own override expansion.
        // The child's closest-depth winner is deny → child is excluded.
        await using var harness = await Harness.CreateAsync();
        var (_, _, _, parentPage) = await harness.SeedTreeAsync();
        var childPage = await harness.CreateChildPageAsync(parentPage);

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{parentPage}", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{childPage}", "deny");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.Contains(parentPage, result.AllowedIds);
        Assert.DoesNotContain(childPage, result.AllowedIds);
    }

    [Fact]
    public async Task MembershipBaselinePlusOverrideDeny_DenyWins()
    {
        // Actor has Contributor membership on a project (would normally see
        // all pages in it) but a single page has an explicit deny. The deny
        // wins; the rest of the project's pages stay accessible.
        await using var harness = await Harness.CreateAsync();
        var (project, _, _, pageA1) = await harness.SeedTreeAsync(extraPages: 1);
        var pageA2 = harness.LastPages[1];

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Contributor);
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/page/{pageA1}", "deny");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.DoesNotContain(pageA1, result.AllowedIds);
        Assert.Contains(pageA2, result.AllowedIds);
    }

    [Fact]
    public async Task WildcardAction_DenyOnSpecificActionUnderWildcardAllow_AppliesPerActionLookup()
    {
        // A wildcard-action grant ("*") covers every action including
        // Delete. An action-specific deny ("delete") on a particular page
        // must apply when computing the access set for that action. View
        // (covered only by the wildcard allow) is unaffected.
        await using var harness = await Harness.CreateAsync();
        var (_, _, _, pageA) = await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.Wildcard, "/*", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.Delete, $"/page/{pageA}", "deny");

        var deletable = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.Delete, default);
        var viewable = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Page, Actions.View, default);

        Assert.DoesNotContain(pageA, deletable.AllowedIds);
        // View has no specific deny → wildcard-allow-no-deny short-circuit
        // applies. Result: unrestricted across the whole system.
        Assert.True(viewable.Unrestricted);
    }

    [Fact]
    public async Task ProjectKind_SlowPathWorksForProjectsTooNotJustPages()
    {
        // The closure for kind=project contains self-rows (project →
        // project, depth 0). The SQL slow path should treat Project the
        // same as any other content kind. Verifies the new SQL doesn't
        // accidentally specialize on Page.
        await using var harness = await Harness.CreateAsync();
        var (projectA, _, _, _) = await harness.SeedTreeAsync();
        var (projectB, _, _, _) = await harness.SeedTreeAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, "/*", "allow");
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View, $"/project/{projectB}", "deny");

        var result = await harness.Authorizer.GetAllowedIdsAsync(
            Principal(actor), ContentKinds.Project, Actions.View, default);

        Assert.False(result.Unrestricted);
        Assert.Contains(projectA, result.AllowedIds);
        Assert.DoesNotContain(projectB, result.AllowedIds);
    }

    private static ClaimsPrincipal Principal(Guid userId) =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            authenticationType: "test"));

    // Test harness: owns the WebApplicationFactory, a DI scope, and the
    // small set of services we use to seed content + grants. Each test
    // gets a fresh harness (so it gets a fresh ContentAuthorizer with an
    // empty per-request cache).
    private sealed class Harness : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;
        private readonly IServiceScope _scope;
        public IContentAuthorizer Authorizer { get; }
        public IDbContextFactory<AutoNateDbContext> DbFactory { get; }
        public IPermissionGrantStore Grants { get; }
        public IContentTreeService Tree { get; }

        // Last seeded "extra" pages from SeedTreeAsync(extraPages: ...)
        // exposed for assertions. Index 0 is always the primary page; any
        // entries beyond that are the extras in creation order.
        public List<Guid> LastPages { get; } = new();

        private Harness(
            AutoNateWebApplicationFactory factory,
            IServiceScope scope,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IPermissionGrantStore grants,
            IContentTreeService tree)
        {
            _factory = factory;
            _scope = scope;
            Authorizer = authorizer;
            DbFactory = dbFactory;
            Grants = grants;
            Tree = tree;
        }

        public static async Task<Harness> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            _ = factory.CreateClient(); // force host start so schema is initialized
            var scope = factory.Services.CreateScope();
            return new Harness(
                factory,
                scope,
                scope.ServiceProvider.GetRequiredService<IContentAuthorizer>(),
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>(),
                scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>(),
                scope.ServiceProvider.GetRequiredService<IContentTreeService>());
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _factory.DisposeAsync();
        }

        // Stand up a project → cabinet → notebook → page tree. Optional
        // `extraPages` adds N more pages to the notebook so tests can
        // assert "sibling stays allowed when one is denied." Returns the
        // ids of the project, cabinet, notebook, and primary page.
        public async Task<(Guid project, Guid cabinet, Guid notebook, Guid primaryPage)>
            SeedTreeAsync(int extraPages = 0, bool deletionsLocked = false)
        {
            LastPages.Clear();
            var actor = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "p-" + Guid.NewGuid().ToString("N")[..8],
                DeletionsLocked = deletionsLocked,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            var cabinet = new Cabinet
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "c", SortOrder = 0, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            var notebook = new Notebook
            {
                Id = Guid.NewGuid(),
                CabinetId = cabinet.Id,
                Name = "n", SortOrder = 0, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            var primaryPage = NewPage(notebook.Id, parentPageId: null, actor, now);

            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Cabinets.Add(cabinet);
                db.Notebooks.Add(notebook);
                db.Pages.Add(primaryPage);
                for (var i = 0; i < extraPages; i++)
                {
                    db.Pages.Add(NewPage(notebook.Id, parentPageId: null, actor, now));
                }
                await db.SaveChangesAsync();
            }

            // Closure rows. One round-trip per entity is fine for tests.
            await BuildClosureAsync(ContentKinds.Project, project.Id);
            await BuildClosureAsync(ContentKinds.Cabinet, cabinet.Id);
            await BuildClosureAsync(ContentKinds.Notebook, notebook.Id);
            LastPages.Add(primaryPage.Id);
            await BuildClosureAsync(ContentKinds.Page, primaryPage.Id);
            if (extraPages > 0)
            {
                await using var db = await DbFactory.CreateDbContextAsync();
                var extras = await db.Pages.AsNoTracking()
                    .Where(p => p.NotebookId == notebook.Id && p.Id != primaryPage.Id)
                    .Select(p => p.Id)
                    .ToListAsync();
                foreach (var pid in extras)
                {
                    LastPages.Add(pid);
                    await BuildClosureAsync(ContentKinds.Page, pid);
                }
            }
            return (project.Id, cabinet.Id, notebook.Id, primaryPage.Id);
        }

        public async Task<Guid> CreateChildPageAsync(Guid parentPageId)
        {
            var actor = Guid.NewGuid();
            var now = DateTime.UtcNow;
            Guid notebookId;
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                notebookId = await db.Pages.AsNoTracking()
                    .Where(p => p.Id == parentPageId)
                    .Select(p => p.NotebookId)
                    .FirstAsync();
            }
            var child = NewPage(notebookId, parentPageId, actor, now);
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                db.Pages.Add(child);
                await db.SaveChangesAsync();
            }
            await BuildClosureAsync(ContentKinds.Page, child.Id);
            return child.Id;
        }

        public async Task AddProjectMemberAsync(Guid projectId, Guid userId, string role)
        {
            var now = DateTime.UtcNow;
            await using var db = await DbFactory.CreateDbContextAsync();
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                UserId = userId,
                Role = role,
                AddedAtUtc = now, AddedBy = userId,
                UpdatedAtUtc = now, UpdatedBy = userId
            });
            await db.SaveChangesAsync();
        }

        public async Task GrantAsync(
            string principalKind, Guid principalId, string action, string selector, string effect)
        {
            await Grants.CreateAsync(
                new CreatePermissionGrantInput(
                    PrincipalKind: principalKind,
                    PrincipalId: principalId.ToString(),
                    Action: action,
                    SelectorString: selector,
                    Effect: effect,
                    Priority: 0),
                principalId);
        }

        public async Task MakeSuperAdminAsync(Guid userId)
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            db.RoleAssignments.Add(new RoleAssignment
            {
                Id = Guid.NewGuid(),
                RoleId = SystemRoles.SuperAdminId,
                PrincipalKind = EntityKinds.User,
                PrincipalId = userId.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = userId
            });
            await db.SaveChangesAsync();
        }

        private async Task BuildClosureAsync(string kind, Guid id)
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            await Tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
        }

        private static Page NewPage(Guid notebookId, Guid? parentPageId, Guid actor, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            NotebookId = notebookId,
            ParentPageId = parentPageId,
            Title = parentPageId is null ? "p" : "child",
            BodyJsonb = "{}",
            CurrentVersionNumber = 1,
            SortOrder = 0,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actor, UpdatedBy = actor
        };
    }
}
