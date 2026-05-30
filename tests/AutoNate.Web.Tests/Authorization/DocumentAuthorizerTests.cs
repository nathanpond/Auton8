using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Phase 2 of the Documents subsystem: Document → Folder (optional) → Project.
// These tests lock down the new behavior:
//  • Commenter role grants View + Comment on a document (no Edit).
//  • Folder-level override deny propagates to nested documents.
//  • A non-member with an explicit document-level override allow can reach
//    the document (share-link scenario).
//  • Restoring a document version captures the current state first and
//    overwrites with the chosen version's title + body.
[Trait("Category", "Integration")]
public sealed class DocumentAuthorizerTests
{
    [Fact]
    public async Task CommenterRole_CanCommentOnDocument_CannotEdit()
    {
        await using var harness = await DocumentHarness.CreateAsync();
        var (project, _, document) = await harness.SeedProjectFolderDocAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Commenter);

        var view = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, document, Actions.View, default);
        var comment = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, document, Actions.Comment, default);
        var edit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, document, Actions.Edit, default);

        Assert.True(view.IsAllowed);
        Assert.True(comment.IsAllowed);
        Assert.False(edit.IsAllowed);
    }

    [Fact]
    public async Task DocumentInFolder_InheritsFolderOverrideDeny()
    {
        // Contributor on the project gives the actor baseline Edit on
        // everything, but an explicit deny on the parent folder strips
        // Edit from the document inside it.
        await using var harness = await DocumentHarness.CreateAsync();
        var (project, folder, document) = await harness.SeedProjectFolderDocAsync();

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Contributor);
        await harness.GrantAsync(EntityKinds.User, actor, Actions.Edit,
            $"/folder/{folder}", "deny");

        var folderEdit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Folder, folder, Actions.Edit, default);
        var docEdit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, document, Actions.Edit, default);

        Assert.False(folderEdit.IsAllowed);
        Assert.False(docEdit.IsAllowed);
    }

    [Fact]
    public async Task DocumentAtProjectRoot_InheritsProjectRoleBaseline()
    {
        // Document at the project root (folder_id IS NULL) walks straight
        // to the project for ancestor resolution. A project member with
        // Viewer role should be able to View it, but not Edit.
        await using var harness = await DocumentHarness.CreateAsync();
        var (project, _, rootDoc) = await harness.SeedProjectFolderDocAsync(rootDocument: true);

        var actor = Guid.NewGuid();
        await harness.AddProjectMemberAsync(project, actor, ProjectRoleNames.Viewer);

        var view = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, rootDoc, Actions.View, default);
        var edit = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, rootDoc, Actions.Edit, default);

        Assert.True(view.IsAllowed);
        Assert.False(edit.IsAllowed);
    }

    [Fact]
    public async Task NonMember_WithDocumentOverrideAllow_CanReachDocument()
    {
        // Share-link / external-collaborator scenario: actor has no project
        // membership, but an explicit allow grant on this specific document
        // surfaces it via the closure rows in content_ancestors.
        await using var harness = await DocumentHarness.CreateAsync();
        var (_, _, docA) = await harness.SeedProjectFolderDocAsync();
        var (_, _, docB) = await harness.SeedProjectFolderDocAsync();

        var actor = Guid.NewGuid();
        await harness.GrantAsync(EntityKinds.User, actor, Actions.View,
            $"/document/{docB}", "allow");

        var docAResult = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, docA, Actions.View, default);
        var docBResult = await harness.Authorizer.AuthorizeAsync(
            Principal(actor), ContentKinds.Document, docB, Actions.View, default);

        Assert.False(docAResult.IsAllowed);
        Assert.True(docBResult.IsAllowed);
    }

    [Fact]
    public async Task VersionRestore_CapturesCurrent_AndOverwritesBodyWithTarget()
    {
        // End-to-end smoke for the version-history mechanic:
        //  1. Create doc with initial body.
        //  2. Snapshot prior + update body (autosave-style).
        //  3. Restore version 1 — should snapshot the now-current state as a
        //     `restore` row and overwrite the document body back to v1.
        await using var harness = await DocumentHarness.CreateAsync();
        var (_, _, documentId) = await harness.SeedProjectFolderDocAsync(
            initialBody: """{"v":1}""");

        var actor = Guid.NewGuid();
        var versionService = harness.Services.GetRequiredService<IContentVersionService>();

        await using (var db = await harness.DbFactory.CreateDbContextAsync())
        {
            await versionService.SnapshotDocumentBeforeChangeAsync(
                db, documentId, "Doc", """{"v":1}""",
                ContentVersionKinds.Autosave, null, actor, DateTime.UtcNow, default);
            var doc = await db.Documents.FirstAsync(d => d.Id == documentId);
            doc.BodyJsonb = """{"v":2}""";
            await db.SaveChangesAsync();
        }

        await using (var db = await harness.DbFactory.CreateDbContextAsync())
        {
            await versionService.RestoreDocumentAsync(
                db, documentId, targetVersionNumber: 1, note: "rollback",
                actor, DateTime.UtcNow, default);
            await db.SaveChangesAsync();
        }

        await using (var db = await harness.DbFactory.CreateDbContextAsync())
        {
            var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId);
            // Postgres stores JSONB in a parsed form; the round-trip text
            // canonicalises (e.g. `{"v":1}` becomes `{"v": 1}`), so compare
            // by parsed value rather than raw string.
            AssertJsonEqual("""{"v":1}""", doc.BodyJsonb);
            // Versions: v1 (initial manual), v2 (autosave of prior v1 body
            // when we patched), v3 (restore snapshot of v2 body). After
            // restore, current_version_number points at v4 (next-to-be-written).
            var versions = await db.DocumentVersions.AsNoTracking()
                .Where(v => v.DocumentId == documentId)
                .OrderBy(v => v.VersionNumber)
                .ToListAsync();
            Assert.Equal(3, versions.Count);
            Assert.Equal(ContentVersionKinds.Manual, versions[0].Kind);
            Assert.Equal(ContentVersionKinds.Autosave, versions[1].Kind);
            Assert.Equal(ContentVersionKinds.Restore, versions[2].Kind);
            AssertJsonEqual("""{"v":2}""", versions[2].BodyJsonb);
        }
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        var ex = JsonSerializer.Serialize(JsonDocument.Parse(expected).RootElement);
        var ac = JsonSerializer.Serialize(JsonDocument.Parse(actual).RootElement);
        Assert.Equal(ex, ac);
    }

    private static ClaimsPrincipal Principal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            "test");
        return new ClaimsPrincipal(identity);
    }

    // Harness adds a SeedProjectFolderDocAsync helper that builds
    // Project → Folder → Document closure rows in one go (or
    // Project → Document at the root if rootDocument: true).
    private sealed class DocumentHarness : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;
        private readonly IServiceScope _scope;
        public IContentAuthorizer Authorizer { get; }
        public IDbContextFactory<AutoNateDbContext> DbFactory { get; }
        public IPermissionGrantStore Grants { get; }
        public IContentTreeService Tree { get; }
        public IServiceProvider Services => _scope.ServiceProvider;

        private DocumentHarness(
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

        public static async Task<DocumentHarness> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            _ = factory.CreateClient();
            var scope = factory.Services.CreateScope();
            return new DocumentHarness(
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

        public async Task<(Guid project, Guid folder, Guid document)>
            SeedProjectFolderDocAsync(bool rootDocument = false, string initialBody = "{}")
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
                Name = "f",
                SortOrder = 0, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            };
            var doc = new Document
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                FolderId = rootDocument ? null : (Guid?)folder.Id,
                Kind = DocumentKinds.Document,
                Title = "Doc",
                BodyJsonb = initialBody,
                CurrentVersionNumber = 1,
                SortOrder = 0,
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
                // Initial manual version row so restore tests have a v1 to
                // target. SnapshotDocument writes the current state as the
                // *prior* version and bumps current_version_number — for the
                // seed case we craft v1 directly to keep numbering at 1.
                db.DocumentVersions.Add(new DocumentVersion
                {
                    Id = Guid.NewGuid(),
                    DocumentId = doc.Id,
                    VersionNumber = 1,
                    Title = doc.Title,
                    BodyJsonb = doc.BodyJsonb,
                    Kind = ContentVersionKinds.Manual,
                    Note = "Initial version",
                    CreatedAtUtc = now,
                    CreatedBy = actor
                });
                doc.CurrentVersionNumber = 2;
                await db.SaveChangesAsync();
            }

            await BuildClosureAsync(ContentKinds.Project, project.Id);
            await BuildClosureAsync(ContentKinds.Folder, folder.Id);
            await BuildClosureAsync(ContentKinds.Document, doc.Id);
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
