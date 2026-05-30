using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Content.Bindings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 6 (Templates) — integration coverage for POST /api/content/documents/
// from-template/{templateId}. Authorization is intentionally disabled in the
// default test factory (see AutoNateWebApplicationFactory), so these tests
// focus on the cloning mechanics: body placeholder rewrite, fresh binding
// ids with cleared resolved values, kind/project validation, and audit
// publication. The permission gates themselves are exercised by
// DocumentAuthorizerTests at the service layer.
[Trait("Category", "Integration")]
public sealed class Phase6TemplateCloneTests
{
    [Fact]
    public async Task CloneFromTemplate_HappyPath_RewritesBindingPlaceholdersAndClonesBindings()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                // The default factory disables authorization but
                // RequirePermission route filters still consult the
                // ContentAuthorizer. Granting SuperAdmin to the seeded
                // admin user lets the auto-logged-in client clear those
                // gates without the test having to grant per-resource
                // permissions piecewise.
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });
        using var client = factory.CreateClient();
        // Dev auto-login attaches the session cookie on the first GET. Issuing
        // an /api/auth/me here primes the client cookie jar so the subsequent
        // POST is authenticated.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var (projectId, templateId, bindingAId, bindingBId) =
            await SeedTemplateWithBindingsAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/content/documents/from-template/{templateId}",
            new ContentDocumentEndpoints.CloneFromTemplateRequest(
                ProjectId: projectId,
                FolderId: null,
                Title: "Cloned doc",
                Description: "from template"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ContentDocumentEndpoints.DocumentDto>();
        Assert.NotNull(dto);
        Assert.Equal("document", dto!.Kind);
        Assert.Equal(templateId, dto.TemplateId);
        Assert.Equal("Cloned doc", dto.Title);
        Assert.Equal("from template", dto.Description);

        // Verify the cloned bindings exist with fresh ids and no resolved values.
        var db = await factory.Services
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>()
            .CreateDbContextAsync();
        await using var _ = db;
        var clonedBindings = await db.DocumentBindings.AsNoTracking()
            .Where(b => b.DocumentId == dto.Id)
            .OrderBy(b => b.Kind)
            .ToListAsync();
        Assert.Equal(2, clonedBindings.Count);
        var clonedAql = clonedBindings.Single(b => b.Kind == DocumentBindingKinds.AqlTable);
        var clonedRecord = clonedBindings.Single(b => b.Kind == DocumentBindingKinds.RecordField);
        Assert.NotEqual(bindingAId, clonedAql.Id);
        Assert.NotEqual(bindingBId, clonedRecord.Id);
        Assert.Null(clonedAql.LastResolvedValueJsonb);
        Assert.Null(clonedAql.LastResolvedAtUtc);
        Assert.Null(clonedRecord.LastResolvedValueJsonb);
        Assert.Null(clonedRecord.LastResolvedAtUtc);
        // ConfigJsonb itself is intentionally copied verbatim — resolution
        // happens against the destination project's data on first refresh.
        Assert.NotNull(clonedAql.ConfigJsonb);
        Assert.NotNull(clonedRecord.ConfigJsonb);

        // Body placeholders must reference the NEW binding ids, not the old ones.
        var clonedDoc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == dto.Id);
        Assert.DoesNotContain(bindingAId.ToString(), clonedDoc.BodyJsonb);
        Assert.DoesNotContain(bindingBId.ToString(), clonedDoc.BodyJsonb);
        Assert.Contains($"{{{{binding:{clonedAql.Id}}}}}", clonedDoc.BodyJsonb);
        Assert.Contains($"{{{{binding:{clonedRecord.Id}}}}}", clonedDoc.BodyJsonb);

        // Initial manual version was snapshotted with a clone-from-template note.
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == dto.Id)
            .ToListAsync();
        Assert.Single(versions);
        Assert.Equal("manual", versions[0].Kind);
        Assert.NotNull(versions[0].Note);
        Assert.Contains(templateId.ToString(), versions[0].Note!);

        // Source template is left untouched — same bindings still in place.
        var sourceBindingsAfter = await db.DocumentBindings.AsNoTracking()
            .Where(b => b.DocumentId == templateId)
            .CountAsync();
        Assert.Equal(2, sourceBindingsAfter);
    }

    [Fact]
    public async Task CloneFromTemplate_RejectsNonTemplateSource()
    {
        // The endpoint must refuse to clone an ordinary Document (kind='document')
        // — only templates are valid sources. Without this guard the gallery
        // could be tricked into making a copy of any doc the user can View.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                // The default factory disables authorization but
                // RequirePermission route filters still consult the
                // ContentAuthorizer. Granting SuperAdmin to the seeded
                // admin user lets the auto-logged-in client clear those
                // gates without the test having to grant per-resource
                // permissions piecewise.
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });
        using var client = factory.CreateClient();
        // Dev auto-login attaches the session cookie on the first GET. Issuing
        // an /api/auth/me here primes the client cookie jar so the subsequent
        // POST is authenticated.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var (projectId, regularDocId) = await SeedRegularDocumentAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/content/documents/from-template/{regularDocId}",
            new ContentDocumentEndpoints.CloneFromTemplateRequest(
                ProjectId: projectId,
                FolderId: null,
                Title: "Should fail",
                Description: null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("not a template", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloneFromTemplate_RejectsCrossProjectDestination()
    {
        // v1 disallows cloning a template into a project other than the one
        // it lives in — bindings reference records / AQL queries that may
        // not exist in the destination project, and cross-project resolve
        // adds an authorization surface we don't want yet.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                // The default factory disables authorization but
                // RequirePermission route filters still consult the
                // ContentAuthorizer. Granting SuperAdmin to the seeded
                // admin user lets the auto-logged-in client clear those
                // gates without the test having to grant per-resource
                // permissions piecewise.
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });
        using var client = factory.CreateClient();
        // Dev auto-login attaches the session cookie on the first GET. Issuing
        // an /api/auth/me here primes the client cookie jar so the subsequent
        // POST is authenticated.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var (_, templateId, _, _) = await SeedTemplateWithBindingsAsync(factory);
        var otherProjectId = await SeedProjectAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/content/documents/from-template/{templateId}",
            new ContentDocumentEndpoints.CloneFromTemplateRequest(
                ProjectId: otherProjectId,
                FolderId: null,
                Title: "Cross-project attempt",
                Description: null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("different project", body, StringComparison.OrdinalIgnoreCase);
        // Sanity: no doc was created in the destination.
        var db = await factory.Services
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>()
            .CreateDbContextAsync();
        await using var _ = db;
        var leaked = await db.Documents.AsNoTracking()
            .Where(d => d.ProjectId == otherProjectId)
            .CountAsync();
        Assert.Equal(0, leaked);
    }

    [Fact]
    public async Task CloneFromTemplate_RejectsEmptyTitle()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                // The default factory disables authorization but
                // RequirePermission route filters still consult the
                // ContentAuthorizer. Granting SuperAdmin to the seeded
                // admin user lets the auto-logged-in client clear those
                // gates without the test having to grant per-resource
                // permissions piecewise.
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });
        using var client = factory.CreateClient();
        // Dev auto-login attaches the session cookie on the first GET. Issuing
        // an /api/auth/me here primes the client cookie jar so the subsequent
        // POST is authenticated.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var (projectId, templateId, _, _) = await SeedTemplateWithBindingsAsync(factory);

        var resp = await client.PostAsJsonAsync(
            $"/api/content/documents/from-template/{templateId}",
            new ContentDocumentEndpoints.CloneFromTemplateRequest(
                ProjectId: projectId,
                FolderId: null,
                Title: "   ",
                Description: null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────

    // Builds a project + template Document with two bindings (one AqlTable,
    // one RecordField), body containing both placeholders, and returns the
    // ids the tests need to assert against.
    private static async Task<(Guid projectId, Guid templateId, Guid bindingAqlId, Guid bindingRecordId)>
        SeedTemplateWithBindingsAsync(AutoNateWebApplicationFactory factory)
    {
        // IContentTreeService is registered as Scoped, so resolve it through
        // a scope rather than the root provider (which CallSiteValidator
        // forbids in Development).
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var treeService = scope.ServiceProvider.GetRequiredService<IContentTreeService>();
        var now = DateTime.UtcNow;
        var actor = Guid.NewGuid();

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "p-" + Guid.NewGuid().ToString("N")[..8],
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actor, UpdatedBy = actor
        };
        var templateId = Guid.NewGuid();
        var aqlBindingId = Guid.NewGuid();
        var recordBindingId = Guid.NewGuid();

        // Minimal ProseMirror doc with two binding placeholder text nodes —
        // enough for the rewrite assertion. Placeholder syntax matches the
        // runtime decoration plugin: {{binding:UUID}} embedded in the text
        // content of a paragraph node. Built via Replace so the literal
        // braces don't fight C#'s raw-string interpolation rules.
        const string bodyTemplate = """
        {
          "type": "doc",
          "content": [
            { "type": "paragraph", "content": [
              { "type": "text", "text": "AQL: __AQL__" }
            ]},
            { "type": "paragraph", "content": [
              { "type": "text", "text": "Record: __RECORD__" }
            ]}
          ]
        }
        """;
        var body = bodyTemplate
            .Replace("__AQL__", $"{{{{binding:{aqlBindingId}}}}}")
            .Replace("__RECORD__", $"{{{{binding:{recordBindingId}}}}}");

        var template = new Document
        {
            Id = templateId,
            ProjectId = project.Id,
            FolderId = null,
            Kind = DocumentKinds.Template,
            Title = "Source template",
            BodyJsonb = body,
            CurrentVersionNumber = 1,
            SortOrder = 0,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actor, UpdatedBy = actor
        };

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Projects.Add(project);
            db.Documents.Add(template);
            db.DocumentBindings.Add(new DocumentBinding
            {
                Id = aqlBindingId,
                DocumentId = templateId,
                Kind = DocumentBindingKinds.AqlTable,
                ConfigJsonb = """{"queryText":"records()","limit":10}""",
                LastResolvedValueJsonb = """{"rows":[]}""",
                LastResolvedAtUtc = now,
                LastResolvedByUserId = actor,
                Label = "All records",
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            });
            db.DocumentBindings.Add(new DocumentBinding
            {
                Id = recordBindingId,
                DocumentId = templateId,
                Kind = DocumentBindingKinds.RecordField,
                ConfigJsonb = $$"""{"recordId":"{{Guid.NewGuid()}}","fieldKey":"title"}""",
                LastResolvedValueJsonb = """{"value":"cached"}""",
                LastResolvedAtUtc = now,
                LastResolvedByUserId = actor,
                Label = "Record title",
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actor, UpdatedBy = actor
            });

            // Initial v1 row so the version-history path doesn't trip over an
            // empty version table when the clone snapshots its baseline.
            db.DocumentVersions.Add(new DocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = templateId,
                VersionNumber = 1,
                Title = template.Title,
                BodyJsonb = template.BodyJsonb,
                Kind = ContentVersionKinds.Manual,
                Note = "Template seed",
                CreatedAtUtc = now,
                CreatedBy = actor
            });
            template.CurrentVersionNumber = 2;
            await db.SaveChangesAsync();

            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Project, project.Id, default);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, templateId, default);
        }

        return (project.Id, templateId, aqlBindingId, recordBindingId);
    }

    private static async Task<(Guid projectId, Guid documentId)>
        SeedRegularDocumentAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var treeService = scope.ServiceProvider.GetRequiredService<IContentTreeService>();
        var now = DateTime.UtcNow;
        var actor = Guid.NewGuid();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "p-" + Guid.NewGuid().ToString("N")[..8],
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actor, UpdatedBy = actor
        };
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            FolderId = null,
            Kind = DocumentKinds.Document,
            Title = "Real doc",
            BodyJsonb = """{"type":"doc","content":[]}""",
            CurrentVersionNumber = 1,
            SortOrder = 0,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actor, UpdatedBy = actor
        };
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Projects.Add(project);
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Project, project.Id, default);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, doc.Id, default);
        }
        return (project.Id, doc.Id);
    }

    private static async Task<Guid> SeedProjectAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        var treeService = scope.ServiceProvider.GetRequiredService<IContentTreeService>();
        var now = DateTime.UtcNow;
        var actor = Guid.NewGuid();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "p-" + Guid.NewGuid().ToString("N")[..8],
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedBy = actor, UpdatedBy = actor
        };
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Project, project.Id, default);
        return project.Id;
    }
}
