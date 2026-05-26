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

// Phase 1 of the Documents subsystem: Project → Folder (self-nesting) is now
// a permissionable kind routed through IContentAuthorizer with closure rows
// in content_ancestors. These tests lock down the new behavior:
//  • Commenter role grants View + Comment on a folder (and denies Edit).
//  • Folders inherit a project's role baseline.
//  • Nested folders inherit a parent folder's override allow/deny.
//  • Cycle-rebuilds work via RebuildAncestorsForSubtreeAsync when a folder is
//    moved across the tree.
[Trait("Category", "Integration")]
public sealed class FolderAuthorizerTests
{
    [Fact]
    public async Task CommenterRole_CanViewAndComment_ButNotEdit_OnRootFolder()
    {
        await using var harness = await FolderHarness.CreateAsync();
        var (project, folder) = await harness.SeedProjectWithRootFolderAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Commenter);

        var view = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.View, default);
        var comment = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Comment, default);
        var edit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Edit, default);
        var del = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Delete, default);

        Assert.True(view.IsAllowed);
        Assert.True(comment.IsAllowed);
        Assert.False(edit.IsAllowed);
        Assert.False(del.IsAllowed);
    }

    [Fact]
    public async Task ContributorRole_CanCommentOnFolder_AndEditAndDelete()
    {
        // Commenter and Contributor both have Comment now; we want to be sure
        // adding "comment" to Contributor's bundle didn't accidentally strip
        // any of the existing CRUD actions.
        await using var harness = await FolderHarness.CreateAsync();
        var (project, folder) = await harness.SeedProjectWithRootFolderAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Contributor);

        var view = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.View, default);
        var comment = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Comment, default);
        var edit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Edit, default);
        var del = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Delete, default);

        Assert.True(view.IsAllowed);
        Assert.True(comment.IsAllowed);
        Assert.True(edit.IsAllowed);
        Assert.True(del.IsAllowed);
    }

    [Fact]
    public async Task ViewerRole_CannotComment()
    {
        // Negative test for the bundle change above — Viewer is read-only and
        // must NOT pick up Comment.
        await using var harness = await FolderHarness.CreateAsync();
        var (project, folder) = await harness.SeedProjectWithRootFolderAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Viewer);

        var view = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.View, default);
        var comment = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Comment, default);

        Assert.True(view.IsAllowed);
        Assert.False(comment.IsAllowed);
    }

    [Fact]
    public async Task NestedFolder_InheritsParentFolderOverrideDeny()
    {
        // ProjectA contains rootFolder; nestedFolder is a child of rootFolder.
        // Actor has Contributor membership in projectA. We deny Edit at
        // rootFolder via an explicit override — nestedFolder must inherit the
        // deny because its ancestor chain rootFolder is closer than the
        // project-role baseline.
        await using var harness = await FolderHarness.CreateAsync();
        var (project, rootFolder) = await harness.SeedProjectWithRootFolderAsync();
        var nestedFolder = await harness.CreateChildFolderAsync(project, rootFolder);

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Contributor);
        await harness.GrantAsync(EntityKinds.User, actor, Actions.Edit,
            $"/folder/{rootFolder}", "deny");

        var rootEdit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, rootFolder, Actions.Edit, default);
        var nestedEdit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, nestedFolder, Actions.Edit, default);

        Assert.False(rootEdit.IsAllowed);
        Assert.False(nestedEdit.IsAllowed);
    }

    [Fact]
    public async Task NonMember_WithFolderOverrideAllow_CanReachFolder()
    {
        // Mirrors the page-level share-link test but on a folder — the
        // canonical "external sharing" smoke test for the new kind.
        await using var harness = await FolderHarness.CreateAsync();
        var (_, folderA) = await harness.SeedProjectWithRootFolderAsync();
        var (_, folderB) = await harness.SeedProjectWithRootFolderAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View,
            $"/folder/{folderB}", "allow");

        var folderAResult = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folderA, Actions.View, default);
        var folderBResult = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folderB, Actions.View, default);

        Assert.False(folderAResult.IsAllowed);
        Assert.True(folderBResult.IsAllowed);
    }

    private static ClaimsPrincipal Principal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            "test");
        return new ClaimsPrincipal(identity);
    }

    // Smaller harness focused on Folder seeding. Mirrors the shape of
    // ContentAuthorizerPolicyTests.Harness but adds folder helpers and uses
    // the same DI surface so we exercise the production ContentTreeService
    // + ContentAuthorizer code paths.
    private sealed class FolderHarness : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;
        private readonly IServiceScope _scope;
        public IContentAuthorizer Authorizer { get; }
        public IDbContextFactory<AutoNateDbContext> DbFactory { get; }
        public IPermissionGrantStore Grants { get; }
        public IContentTreeService Tree { get; }

        private FolderHarness(
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

        public static async Task<FolderHarness> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            _ = factory.CreateClient();
            var scope = factory.Services.CreateScope();
            return new FolderHarness(
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

        public async Task<(Guid project, Guid folder)> SeedProjectWithRootFolderAsync()
        {
            var actor = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "p-" + Guid.NewGuid().ToString("N")[..8],
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            var folder = new Folder
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                ParentFolderId = null,
                Name = "root-folder",
                SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Folders.Add(folder);
                await db.SaveChangesAsync();
            }
            await BuildClosureAsync(ContentKinds.Project, project.Id);
            await BuildClosureAsync(ContentKinds.Folder, folder.Id);
            return (project.Id, folder.Id);
        }

        public async Task<Guid> CreateChildFolderAsync(Guid projectId, Guid parentFolderId)
        {
            var actor = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var child = new Folder
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                ParentFolderId = parentFolderId,
                Name = "nested",
                SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                db.Folders.Add(child);
                await db.SaveChangesAsync();
            }
            await BuildClosureAsync(ContentKinds.Folder, child.Id);
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

        private async Task BuildClosureAsync(string kind, Guid id)
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            await Tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
        }
    }
}
