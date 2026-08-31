using System.Net;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// #21: GET /api/content/locator/{n} resolved a sequential long to
// (kind, id, ancestor chain) with no authorization at all, so
// `for i in 1..N` handed any signed-in user a complete map of the tenant's
// content tree — including entities every other endpoint would 404 — plus the
// GUIDs to feed those endpoints.
[Trait("Category", "Integration")]
public sealed class ContentLocatorEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    [Fact]
    public async Task Locator_WithoutViewGrant_IsNotFoundForEveryKind()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var seeded = await SeedAsync(factory);
        var client = await SignedInClientAsync(factory);

        foreach (var (label, locator) in seeded.Locators)
        {
            var resp = await client.GetAsync($"/api/content/locator/{locator}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

            // A denial must not leak the id either — that was the payload.
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain(seeded.ProjectId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.PageId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrEmpty(label));
        }
    }

    // A denial has to be indistinguishable from a locator that does not exist,
    // otherwise the endpoint still enumerates the tree by status code alone.
    [Fact]
    public async Task DeniedLocator_IsIndistinguishableFromAnUnusedOne()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var seeded = await SeedAsync(factory);
        var client = await SignedInClientAsync(factory);

        var denied = await client.GetAsync($"/api/content/locator/{seeded.Locators[^1].Locator}");
        var unused = await client.GetAsync($"/api/content/locator/{long.MaxValue - 12345}");

        Assert.Equal(unused.StatusCode, denied.StatusCode);
        Assert.Equal(
            await unused.Content.ReadAsStringAsync(),
            (await denied.Content.ReadAsStringAsync())
                .Replace(seeded.Locators[^1].Locator.ToString(), (long.MaxValue - 12345).ToString(), StringComparison.Ordinal));
    }

    // Positive control: the guard must refuse the unauthorized, not everyone.
    // Content permissions come from project membership / the content
    // authorizer rather than an entity selector, so this uses the backfilled
    // SuperAdmin actor — the point is that an authorized caller still resolves.
    [Fact]
    public async Task Locator_ForAnAuthorizedCaller_StillResolvesThePage()
    {
        var config = EnforceConfig();
        config["Authorization:AssignSuperAdminToAllExistingUsers"] = "true";

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(config);
        var seeded = await SeedAsync(factory);
        var client = await SignedInClientAsync(factory);

        var pageLocator = seeded.Locators.Single(l => l.Label == "page").Locator;
        var resp = await client.GetAsync($"/api/content/locator/{pageLocator}");

        resp.EnsureSuccessStatusCode();
        Assert.Contains(
            seeded.PageId.ToString(),
            await resp.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----

    private static async Task<HttpClient> SignedInClientAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return client;
    }

    private sealed record Seeded(
        Guid ProjectId, Guid PageId, IReadOnlyList<(string Label, long Locator)> Locators);

    private static async Task<Seeded> SeedAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var dbFactory = sp.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var tree = sp.GetRequiredService<IContentTreeService>();

        var now = DateTime.UtcNow;
        var actorId = AdminUserId;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "locator-tests", DeletionsLocked = false, IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };
        var cabinet = new Cabinet
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "cab", IsArchived = false, SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };
        var notebook = new Notebook
        {
            Id = Guid.NewGuid(), CabinetId = cabinet.Id, Name = "nb", IsArchived = false, SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };
        var page = new Page
        {
            Id = Guid.NewGuid(), NotebookId = notebook.Id, ParentPageId = null, Title = "p",
            BodyJsonb = "{}", CurrentVersionNumber = 1, SortOrder = 0, IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };
        var note = new Note
        {
            Id = Guid.NewGuid(), PageId = page.Id, PageNoteIndex = 1, NoteKind = "richtext",
            Title = "n", ContentJsonb = "{}", CurrentVersionNumber = 1, SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Projects.Add(project);
            db.Cabinets.Add(cabinet);
            db.Notebooks.Add(notebook);
            db.Pages.Add(page);
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }

        foreach (var (kind, id) in new[]
        {
            (ContentKinds.Project, project.Id),
            (ContentKinds.Cabinet, cabinet.Id),
            (ContentKinds.Notebook, notebook.Id),
            (ContentKinds.Page, page.Id)
        })
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
        }

        // Locators are DB-assigned from a sequence; read them back.
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var locators = new List<(string, long)>
            {
                ("project", await db.Projects.AsNoTracking().Where(p => p.Id == project.Id).Select(p => p.Locator).FirstAsync()),
                ("cabinet", await db.Cabinets.AsNoTracking().Where(c => c.Id == cabinet.Id).Select(c => c.Locator).FirstAsync()),
                ("notebook", await db.Notebooks.AsNoTracking().Where(n => n.Id == notebook.Id).Select(n => n.Locator).FirstAsync()),
                ("page", await db.Pages.AsNoTracking().Where(p => p.Id == page.Id).Select(p => p.Locator).FirstAsync()),
                ("note", await db.Notes.AsNoTracking().Where(n => n.Id == note.Id).Select(n => n.Locator).FirstAsync()),
            };
            return new Seeded(project.Id, page.Id, locators);
        }
    }
}
