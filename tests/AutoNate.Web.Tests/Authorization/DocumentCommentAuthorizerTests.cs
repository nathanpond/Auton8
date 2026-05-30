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

// Phase 4 — comments + Commenter role.
//
// The Commenter project role grants View + Comment, never Edit. These
// tests lock in:
//   • Commenter can authorize Document.View + Document.Comment, can't
//     authorize Document.Edit.
//   • Contributor + Owner roles still get Document.Comment as part of
//     their bundle (so they can post comments without an explicit grant).
//   • Viewer role is denied Document.Comment.
//   • Override grants of Comment on a non-project-member surface via
//     the closure rows (the canonical share-link scenario).
[Trait("Category", "Integration")]
public sealed class DocumentCommentAuthorizerTests
{
    [Fact]
    public async Task CommenterRole_CanCommentOnDocument_ButNotEdit()
    {
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Commenter);

        Assert.True((await harness.Authorize(actor, document, Actions.View)).IsAllowed);
        Assert.True((await harness.Authorize(actor, document, Actions.Comment)).IsAllowed);
        Assert.False((await harness.Authorize(actor, document, Actions.Edit)).IsAllowed);
    }

    [Fact]
    public async Task ContributorRole_HasCommentInBundle()
    {
        // Sanity: Contributor's role-bundle already includes Comment
        // (set up in Phase 1). This test guards against a future change
        // accidentally stripping comment from the bundle.
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Contributor);

        Assert.True((await harness.Authorize(actor, document, Actions.Comment)).IsAllowed);
    }

    [Fact]
    public async Task ViewerRole_CannotComment()
    {
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Viewer);

        Assert.True((await harness.Authorize(actor, document, Actions.View)).IsAllowed);
        Assert.False((await harness.Authorize(actor, document, Actions.Comment)).IsAllowed);
    }

    [Fact]
    public async Task NonMember_WithCommentOverride_CanComment_NotEdit()
    {
        // The "external collaborator" share-link case: a user with no
        // project membership gets an explicit Comment grant on one
        // document. They can view and comment on JUST that document,
        // and can't edit it.
        await using var harness = await Harness.CreateAsync();
        var (_, _, docA) = await harness.SeedAsync();
        var (_, _, docB) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        // Comment + View both need to be granted — Document.Comment alone
        // doesn't imply Document.View in the override model (each action
        // is independently grantable).
        await harness.GrantAsync(actor, Actions.View, $"/document/{docB}", "allow");
        await harness.GrantAsync(actor, Actions.Comment, $"/document/{docB}", "allow");

        Assert.False((await harness.Authorize(actor, docA, Actions.Comment)).IsAllowed);
        Assert.True((await harness.Authorize(actor, docB, Actions.View)).IsAllowed);
        Assert.True((await harness.Authorize(actor, docB, Actions.Comment)).IsAllowed);
        Assert.False((await harness.Authorize(actor, docB, Actions.Edit)).IsAllowed);
    }

    private static ClaimsPrincipal Principal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            "test");
        return new ClaimsPrincipal(identity);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;
        private readonly IServiceScope _scope;
        public IContentAuthorizer Authorizer { get; }
        public IDbContextFactory<AutoNateDbContext> DbFactory { get; }
        public IPermissionGrantStore Grants { get; }
        public IContentTreeService Tree { get; }

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
            _ = factory.CreateClient();
            var scope = factory.Services.CreateScope();
            return new Harness(
                factory, scope,
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

        public Task<AuthDecision> Authorize(Guid userId, Guid resourceId, string action) =>
            Authorizer.AuthorizeAsync(
                Principal(userId), ContentKinds.Document, resourceId, action, default);

        public async Task<(Guid project, Guid folder, Guid document)> SeedAsync()
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
                Id = Guid.NewGuid(), ProjectId = project.Id, ParentFolderId = null,
                Name = "f", SortOrder = 0, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            var doc = new Document
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id, FolderId = folder.Id,
                Kind = DocumentKinds.Document,
                Title = "Doc", BodyJsonb = "{}",
                CurrentVersionNumber = 1, SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            await using (var db = await DbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Folders.Add(folder);
                db.Documents.Add(doc);
                await db.SaveChangesAsync();
            }
            await Tree.InsertSelfWithAncestorsAsync(
                await DbFactory.CreateDbContextAsync(), ContentKinds.Project, project.Id, default);
            await Tree.InsertSelfWithAncestorsAsync(
                await DbFactory.CreateDbContextAsync(), ContentKinds.Folder, folder.Id, default);
            await Tree.InsertSelfWithAncestorsAsync(
                await DbFactory.CreateDbContextAsync(), ContentKinds.Document, doc.Id, default);
            return (project.Id, folder.Id, doc.Id);
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

        public async Task GrantAsync(Guid principalId, string action, string selector, string effect)
        {
            await Grants.CreateAsync(
                new CreatePermissionGrantInput(
                    PrincipalKind: EntityKinds.User,
                    PrincipalId: principalId.ToString(),
                    Action: action,
                    SelectorString: selector,
                    Effect: effect,
                    Priority: 0),
                principalId);
        }
    }
}
