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

// Phase 5 — live data bindings. These tests lock down which roles can
// CREATE/EDIT bindings vs trigger REFRESH. The split exists so a future
// UX could let Commenters refresh (recompute cached values without
// changing the document body) without granting them Edit — Phase 5 v1
// bundles RefreshBindings into Contributor + Owner only.
[Trait("Category", "Integration")]
public sealed class DocumentBindingAuthorizerTests
{
    [Fact]
    public async Task ContributorRole_CanEditAndRefreshBindings()
    {
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Contributor);

        Assert.True((await harness.Authorize(actor, document, Actions.Edit)).IsAllowed);
        Assert.True((await harness.Authorize(actor, document, Actions.RefreshBindings)).IsAllowed);
    }

    [Fact]
    public async Task OwnerRole_CanEditAndRefreshBindings()
    {
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Owner);

        Assert.True((await harness.Authorize(actor, document, Actions.Edit)).IsAllowed);
        Assert.True((await harness.Authorize(actor, document, Actions.RefreshBindings)).IsAllowed);
    }

    [Fact]
    public async Task CommenterRole_CannotEditOrRefreshBindings()
    {
        // Phase 5 v1 keeps RefreshBindings inside the Editor/Owner bundle.
        // The Commenter role gets View + Comment but NOT RefreshBindings.
        // If we ever loosen this (so commenters can recompute live data
        // without editing the body), this test will fail and force a
        // deliberate decision.
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Commenter);

        Assert.False((await harness.Authorize(actor, document, Actions.Edit)).IsAllowed);
        Assert.False((await harness.Authorize(actor, document, Actions.RefreshBindings)).IsAllowed);
    }

    [Fact]
    public async Task ViewerRole_CannotRefreshBindings()
    {
        await using var harness = await Harness.CreateAsync();
        var (project, _, document) = await harness.SeedAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Viewer);

        Assert.True((await harness.Authorize(actor, document, Actions.View)).IsAllowed);
        Assert.False((await harness.Authorize(actor, document, Actions.RefreshBindings)).IsAllowed);
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
        public IContentTreeService Tree { get; }

        private Harness(
            AutoNateWebApplicationFactory factory,
            IServiceScope scope,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService tree)
        {
            _factory = factory;
            _scope = scope;
            Authorizer = authorizer;
            DbFactory = dbFactory;
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
    }
}
