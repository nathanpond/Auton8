using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 7 (DOCX/DOTX import) — coverage for POST /api/content/documents/import
// and the import-buffer fetch + discard endpoints. The endpoint dispatches
// to Document.Kind based on file extension (.docx → 'document',
// .dotx → 'template') and stashes the uploaded bytes to disk via
// IDocumentImportStorage; the editor's first mount fetches them back
// through GET /{id}/import-buffer and DELETE-s after the first autosave.
//
// docx-editor owns the actual OOXML parser; the backend only validates
// that the upload is a ZIP-family container (the OOXML wrapper), enforces
// size, and routes the kind discriminator. So these tests build a
// minimal valid ZIP in-memory rather than shipping a real .docx fixture
// — anything ZIP-shaped passes the magic-byte sniff and the tests can
// focus on the dispatch + persistence + cleanup behaviour.
[Trait("Category", "Integration")]
public sealed class Phase7DocumentImportTests
{
    [Fact]
    public async Task ImportDocx_CreatesDocument_StashesBytes()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var projectId = await SeedProjectAsync(factory);
        var bytes = BuildMinimalZip();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", "report.docx");
        content.Add(new StringContent(projectId.ToString()), "projectId");
        content.Add(new StringContent("My imported doc"), "title");

        var resp = await client.PostAsync("/api/content/documents/import", content);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ContentDocumentEndpoints.DocumentDto>();
        Assert.NotNull(dto);
        Assert.Equal("document", dto!.Kind);
        Assert.Equal("My imported doc", dto.Title);
        Assert.Equal(projectId, dto.ProjectId);

        // Stash must be readable through the matching GET endpoint.
        var bufferResp = await client.GetAsync($"/api/content/documents/{dto.Id}/import-buffer");
        Assert.Equal(HttpStatusCode.OK, bufferResp.StatusCode);
        var bufferBytes = await bufferResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes.Length, bufferBytes.Length);

        // DELETE clears the stash; a subsequent GET should be 404.
        var deleteResp = await client.DeleteAsync($"/api/content/documents/{dto.Id}/import-buffer");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
        var afterDelete = await client.GetAsync($"/api/content/documents/{dto.Id}/import-buffer");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task ImportDotx_CreatesTemplate()
    {
        // .dotx is the OOXML template flavour. docx-editor parses both
        // identically (same wordprocessingml container); the import
        // endpoint is responsible for routing it to Document.Kind =
        // 'template' so it lands in the cross-project gallery rather
        // than a folder grid.
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var projectId = await SeedProjectAsync(factory);
        var bytes = BuildMinimalZip();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template");
        content.Add(fileContent, "file", "report.dotx");
        content.Add(new StringContent(projectId.ToString()), "projectId");

        var resp = await client.PostAsync("/api/content/documents/import", content);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ContentDocumentEndpoints.DocumentDto>();
        Assert.NotNull(dto);
        Assert.Equal("template", dto!.Kind);
        // Title defaulted from the file's base name (no explicit title sent).
        Assert.Equal("report", dto.Title);
    }

    [Fact]
    public async Task Import_RejectsUnknownExtension()
    {
        // Legacy .doc / .pdf / .txt etc. are not OOXML — docx-editor
        // can't parse them. We reject before sniffing because the
        // extension dispatch is what picks the Document.Kind.
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var projectId = await SeedProjectAsync(factory);
        var bytes = BuildMinimalZip();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", "report.zip");
        content.Add(new StringContent(projectId.ToString()), "projectId");

        var resp = await client.PostAsync("/api/content/documents/import", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(".docx and .dotx", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_RejectsNonOoxmlBytes()
    {
        // A file ending in .docx but holding random non-ZIP bytes
        // shouldn't slip past the sniff. docx-editor would throw on a
        // malformed buffer; we'd rather fail loudly at upload time.
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var projectId = await SeedProjectAsync(factory);
        var bytes = "Not a real docx file"u8.ToArray();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", "fake.docx");
        content.Add(new StringContent(projectId.ToString()), "projectId");

        var resp = await client.PostAsync("/api/content/documents/import", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ooxml", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_MissingProjectIdRejected()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var bytes = BuildMinimalZip();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", "x.docx");
        // No projectId form field.

        var resp = await client.PostAsync("/api/content/documents/import", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Builds the minimum bytes the magic-byte sniff recognises as ZIP:
    // a real PKZIP archive with one empty entry. We don't bother writing
    // valid OOXML internals because the endpoint stops at "is this ZIP-
    // family?" — docx-editor owns the OOXML parser, and these tests
    // never hand the buffer to that parser.
    private static byte[] BuildMinimalZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("[Content_Types].xml");
        }
        return ms.ToArray();
    }

    private static async Task<AutoNateWebApplicationFactory> CreateFactoryAsync()
    {
        // Point the import stash at a fresh temp directory per factory so
        // parallel xUnit runs don't trample each other's stashes. Also
        // grant SuperAdmin to the seeded admin user — same pattern Phase
        // 6 tests use so the auto-logged-in admin clears RequirePermission
        // filters without per-resource grants.
        var stashRoot = Path.Combine(
            Path.GetTempPath(),
            "autonate-document-imports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stashRoot);

        return await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                ["DocumentImports:RootPath"] = stashRoot,
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        // Dev auto-login attaches the session cookie on the first GET.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
    }

    private static async Task<Guid> SeedProjectAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
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
