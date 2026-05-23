using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Query;

[Trait("Category", "Integration")]
public sealed class NotesQueryEndpointTests
{
    private sealed record ExecuteQueryResponseDto(
        List<ColumnDto> Columns,
        List<Dictionary<string, JsonElement>> Rows,
        long TotalCount,
        bool Truncated,
        long DurationMs);

    private sealed record ColumnDto(string Name, string DataType);

    [Fact]
    public async Task FromNotes_OnEmpty_ReturnsSchemaColumns()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new { query = "FROM Notes" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        var columnNames = body!.Columns.Select(c => c.Name).ToList();
        Assert.Contains("Id", columnNames);
        Assert.Contains("Type", columnNames);
        Assert.Contains("SubType", columnNames);
        Assert.Contains("Name", columnNames);
        Assert.Contains("DateCreated", columnNames);
        Assert.Contains("CreatedBy", columnNames);
        Assert.Contains("FullPath", columnNames);
    }

    [Fact]
    public async Task FromNotes_FiltersByType_AndContains()
    {
        await using var factory = await CreateFactoryWithSuperAdminAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedHierarchyAsync(factory);

        // The Tundra cabinet should be the only one matched.
        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Notes WHERE Type = \"Cabinet\" AND Name ~ \"Tundra\" ORDER BY Name COLUMNS(Name)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        Assert.Single(body!.Rows);
        Assert.Equal("Tundra Cabinet", body.Rows[0]["Name"].GetString());
    }

    [Fact]
    public async Task FromNotes_PARENT_Returns_DirectChildren()
    {
        await using var factory = await CreateFactoryWithSuperAdminAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var seed = await SeedHierarchyAsync(factory);

        // PARENT(<cabinetLocator>) returns just the notebooks directly under
        // that cabinet — not the deeper pages/notes.
        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = $"FROM Notes WHERE PARENT({seed.TundraCabinetLocator}) ORDER BY Name COLUMNS(Name, Type)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        var names = body!.Rows.Select(r => r["Name"].GetString()).ToList();
        var types = body.Rows.Select(r => r["Type"].GetString()).ToList();
        Assert.Equal(new[] { "Tundra Notebook" }, names);
        Assert.Equal(new[] { "Notebook" }, types);
    }

    [Fact]
    public async Task FromNotes_ISDESCENDENTOF_Returns_AllDescendants()
    {
        await using var factory = await CreateFactoryWithSuperAdminAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var seed = await SeedHierarchyAsync(factory);

        // ISDESCENDENTOF(<projectLocator>) returns the cabinet, the notebook,
        // the page, and the note hanging off the page — but not the project
        // itself or the other (unrelated) project.
        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = $"FROM Notes WHERE ISDESCENDENTOF({seed.TundraProjectLocator}) ORDER BY Type COLUMNS(Name, Type)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        var rows = body!.Rows.Select(r => (Name: r["Name"].GetString(), Type: r["Type"].GetString())).ToList();
        Assert.Contains(rows, r => r.Name == "Tundra Cabinet" && r.Type == "Cabinet");
        Assert.Contains(rows, r => r.Name == "Tundra Notebook" && r.Type == "Notebook");
        Assert.Contains(rows, r => r.Name == "Tundra Page" && r.Type == "Page");
        Assert.Contains(rows, r => r.Name == "Tundra Note" && r.Type == "Note");
        Assert.DoesNotContain(rows, r => r.Name == "Tundra Project");
        Assert.DoesNotContain(rows, r => r.Name == "Sahara Project");
    }

    [Fact]
    public async Task FromNotes_CountChildrenAndDescendents_AsProjections()
    {
        await using var factory = await CreateFactoryWithSuperAdminAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var seed = await SeedHierarchyAsync(factory);

        // For the Tundra Project: 1 direct child (cabinet) and 4 descendants
        // (cabinet + notebook + page + note).
        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = $"FROM Notes WHERE ID = {seed.TundraProjectLocator} COLUMNS(Name, COUNTCHILDREN() AS Children, COUNTDESCENDENTS() AS Descendents)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        Assert.Single(body!.Rows);
        var row = body.Rows[0];
        Assert.Equal("Tundra Project", row["Name"].GetString());
        Assert.Equal(1, row["Children"].GetInt32());
        Assert.Equal(4, row["Descendents"].GetInt32());
    }

    [Fact]
    public async Task FromNotes_FullPath_Reflects_Hierarchy()
    {
        await using var factory = await CreateFactoryWithSuperAdminAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var seed = await SeedHierarchyAsync(factory);

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = $"FROM Notes WHERE ID = {seed.TundraNoteLocator} COLUMNS(Name, FullPath)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        Assert.Single(body!.Rows);
        Assert.Equal(
            "Tundra Project / Tundra Cabinet / Tundra Notebook / Tundra Page / Tundra Note",
            body.Rows[0]["FullPath"].GetString());
    }

    [Fact]
    public async Task FromNotes_GroupByType_Counts_PerKind()
    {
        await using var factory = await CreateFactoryWithSuperAdminAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        await SeedHierarchyAsync(factory);

        // Grouping by Type yields one bucket per distinct Type seen. The
        // seed adds 2 projects + 1 cabinet + 1 notebook + 1 page + 1 note;
        // the schema initializer may also bootstrap a default project, so we
        // assert the seed-relative counts (Projects strictly the most) and
        // that each leaf kind contributes exactly one row.
        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Notes ORDER BY COUNT() DESC COLUMNS(Type, COUNT() AS Count) GROUP(Type)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        var byType = body!.Rows.ToDictionary(
            r => r["Type"].GetString()!,
            r => r["Count"].GetInt64());
        Assert.True(byType.ContainsKey("Project"));
        Assert.Equal(1, byType["Cabinet"]);
        Assert.Equal(1, byType["Notebook"]);
        Assert.Equal(1, byType["Page"]);
        Assert.Equal(1, byType["Note"]);
        Assert.True(byType["Project"] >= 2);
        // ORDER BY COUNT() DESC: the largest bucket must come first.
        Assert.Equal("Project", body.Rows[0]["Type"].GetString());
        Assert.Equal(byType["Project"], body.Rows[0]["Count"].GetInt64());
    }

    [Fact]
    public async Task FromNotes_UnknownType_Returns_Friendly_Error()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Notes WHERE Type = \"Banana\""
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var errors = doc.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("Banana", StringComparison.Ordinal));
    }

    // ---- Seed -----------------------------------------------------------

    // The dev auto-login user (admin) doesn't get the SuperAdmin role unless
    // the schema-init backfill is enabled. Without it, IContentAuthorizer
    // returns ContentAccessSet.Empty and these tests see no rows.
    private static Task<AutoNateWebApplicationFactory> CreateFactoryWithSuperAdminAsync() =>
        AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
        });

    private sealed record SeedResult(
        long TundraProjectLocator,
        long TundraCabinetLocator,
        long TundraNotebookLocator,
        long TundraPageLocator,
        long TundraNoteLocator);

    private static async Task<SeedResult> SeedHierarchyAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var tree = scope.ServiceProvider.GetRequiredService<IContentTreeService>();

        var actorId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Two projects: one drives the assertions, the other proves filters
        // exclude unrelated rows.
        var tundra = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Tundra Project",
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actorId, UpdatedBy = actorId
        };
        var sahara = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Sahara Project",
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actorId, UpdatedBy = actorId
        };
        var cabinet = new Cabinet
        {
            Id = Guid.NewGuid(), ProjectId = tundra.Id,
            Name = "Tundra Cabinet", SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actorId, UpdatedBy = actorId
        };
        var notebook = new Notebook
        {
            Id = Guid.NewGuid(), CabinetId = cabinet.Id,
            Name = "Tundra Notebook", SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actorId, UpdatedBy = actorId
        };
        var page = new Page
        {
            Id = Guid.NewGuid(), NotebookId = notebook.Id,
            Title = "Tundra Page", BodyJsonb = "{}",
            CurrentVersionNumber = 1, SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actorId, UpdatedBy = actorId
        };
        var note = new Note
        {
            Id = Guid.NewGuid(), PageId = page.Id,
            PageNoteIndex = 1, NoteKind = "richtext",
            Title = "Tundra Note", ContentJsonb = "{}",
            CurrentVersionNumber = 1, SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actorId, UpdatedBy = actorId
        };

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Projects.Add(tundra);
            db.Projects.Add(sahara);
            db.Cabinets.Add(cabinet);
            db.Notebooks.Add(notebook);
            db.Pages.Add(page);
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }

        // Populate content_ancestors for the four content kinds. Notes don't
        // participate in the closure — the entity synthesizes their parent
        // edge from PageId.
        foreach (var (kind, id) in new[]
        {
            (ContentKinds.Project, tundra.Id),
            (ContentKinds.Project, sahara.Id),
            (ContentKinds.Cabinet, cabinet.Id),
            (ContentKinds.Notebook, notebook.Id),
            (ContentKinds.Page, page.Id)
        })
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
        }

        // Re-read the locators (assigned by the BIGINT sequence on INSERT).
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var pj = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == tundra.Id);
            var c = await db.Cabinets.AsNoTracking().FirstAsync(x => x.Id == cabinet.Id);
            var n = await db.Notebooks.AsNoTracking().FirstAsync(x => x.Id == notebook.Id);
            var p = await db.Pages.AsNoTracking().FirstAsync(x => x.Id == page.Id);
            var no = await db.Notes.AsNoTracking().FirstAsync(x => x.Id == note.Id);
            return new SeedResult(pj.Locator, c.Locator, n.Locator, p.Locator, no.Locator);
        }
    }
}
