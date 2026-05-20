using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ContentTreeServiceTests
{
    [Fact]
    public async Task RebuildAncestorsForSubtreeAsync_MovesEntireSubtreeUnderNewProject()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var tree = sp.GetRequiredService<IContentTreeService>();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        // Build two projects and one full hierarchy hung off project A:
        //
        //   projectA
        //     └── cabinet
        //           └── notebook
        //                 └── page
        //                       └── childPage
        //
        // projectB is the move destination.
        var actorId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var projectA = MakeProject(actorId, now, "tree-test-A");
        var projectB = MakeProject(actorId, now, "tree-test-B");
        var cabinet = MakeCabinet(projectA.Id, actorId, now);
        var notebook = MakeNotebook(cabinet.Id, actorId, now);
        var page = MakePage(notebook.Id, parentPageId: null, actorId, now);
        var childPage = MakePage(notebook.Id, parentPageId: page.Id, actorId, now);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Projects.Add(projectA);
            db.Projects.Add(projectB);
            db.Cabinets.Add(cabinet);
            db.Notebooks.Add(notebook);
            db.Pages.Add(page);
            db.Pages.Add(childPage);
            await db.SaveChangesAsync();
        }

        // Initial closure for every node — uses the existing single-entity
        // insert path. Closure is then known-correct before we touch it.
        foreach (var (kind, id) in new[]
        {
            (ContentKinds.Project, projectA.Id),
            (ContentKinds.Project, projectB.Id),
            (ContentKinds.Cabinet, cabinet.Id),
            (ContentKinds.Notebook, notebook.Id),
            (ContentKinds.Page, page.Id),
            (ContentKinds.Page, childPage.Id),
        })
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
        }

        // Move the cabinet to project B and rebuild. The whole subtree
        // (cabinet → notebook → page → childPage) should re-anchor under
        // projectB; projectA should no longer appear as an ancestor of any
        // of those nodes.
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var c = await db.Cabinets.FirstAsync(x => x.Id == cabinet.Id);
            c.ProjectId = projectB.Id;
            await db.SaveChangesAsync();
            await tree.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Cabinet, cabinet.Id, default);
        }

        // Verify each subtree node has projectB as its top ancestor and the
        // depths line up. Self rows (depth 0) must remain present too.
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var ancestors = await db.ContentAncestors.AsNoTracking()
                .Where(ca =>
                    (ca.DescendantKind == ContentKinds.Cabinet && ca.DescendantId == cabinet.Id) ||
                    (ca.DescendantKind == ContentKinds.Notebook && ca.DescendantId == notebook.Id) ||
                    (ca.DescendantKind == ContentKinds.Page &&
                        (ca.DescendantId == page.Id || ca.DescendantId == childPage.Id)))
                .ToListAsync();

            // projectA must not appear anywhere in the moved subtree's chains.
            Assert.DoesNotContain(ancestors, a =>
                a.AncestorKind == ContentKinds.Project && a.AncestorId == projectA.Id);

            void AssertChain(string descendantKind, Guid descendantId,
                (string Kind, Guid Id)[] expectedByDepth)
            {
                var chain = ancestors
                    .Where(a => a.DescendantKind == descendantKind && a.DescendantId == descendantId)
                    .OrderBy(a => a.Depth)
                    .Select(a => (a.AncestorKind, a.AncestorId))
                    .ToArray();
                Assert.Equal(expectedByDepth, chain);
            }

            AssertChain(ContentKinds.Cabinet, cabinet.Id, new[]
            {
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Project, projectB.Id)
            });
            AssertChain(ContentKinds.Notebook, notebook.Id, new[]
            {
                (ContentKinds.Notebook, notebook.Id),
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Project, projectB.Id)
            });
            AssertChain(ContentKinds.Page, page.Id, new[]
            {
                (ContentKinds.Page, page.Id),
                (ContentKinds.Notebook, notebook.Id),
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Project, projectB.Id)
            });
            AssertChain(ContentKinds.Page, childPage.Id, new[]
            {
                (ContentKinds.Page, childPage.Id),
                (ContentKinds.Page, page.Id),
                (ContentKinds.Notebook, notebook.Id),
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Project, projectB.Id)
            });
        }
    }

    private static Project MakeProject(Guid actorId, DateTime now, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DeletionsLocked = false,
        IsArchived = false,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        CreatedBy = actorId,
        UpdatedBy = actorId
    };

    private static Cabinet MakeCabinet(Guid projectId, Guid actorId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Name = "cab",
        IsArchived = false,
        SortOrder = 0,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        CreatedBy = actorId,
        UpdatedBy = actorId
    };

    private static Notebook MakeNotebook(Guid cabinetId, Guid actorId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        CabinetId = cabinetId,
        Name = "nb",
        IsArchived = false,
        SortOrder = 0,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        CreatedBy = actorId,
        UpdatedBy = actorId
    };

    private static Page MakePage(Guid notebookId, Guid? parentPageId, Guid actorId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        NotebookId = notebookId,
        ParentPageId = parentPageId,
        Title = parentPageId is null ? "root" : "child",
        BodyJsonb = "{}",
        CurrentVersionNumber = 1,
        SortOrder = 0,
        IsArchived = false,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        CreatedBy = actorId,
        UpdatedBy = actorId
    };
}
