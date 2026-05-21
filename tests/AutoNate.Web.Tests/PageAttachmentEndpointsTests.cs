using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class PageAttachmentEndpointsTests
{
    [Fact]
    public async Task Upload_ValidPngWithMatchingMime_Returns201()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        using var content = BuildMultipart(BuildPngBytes(), "image/png", "image.png");
        var response = await ctx.Client.PostAsync(
            $"/api/content/pages/{pageId}/attachments/", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_HtmlBytesClaimingPng_RejectedBySniff()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        var html = Encoding.UTF8.GetBytes(
            "<html><body><script>alert('xss')</script></body></html>");
        using var content = BuildMultipart(html, "image/png", "xss.png");

        var response = await ctx.Client.PostAsync(
            $"/api/content/pages/{pageId}/attachments/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsupported file format", body);
    }

    [Fact]
    public async Task Upload_JpegBytesClaimingPng_RejectedAsSniffMismatch()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        using var content = BuildMultipart(jpeg, "image/png", "fake.png");

        var response = await ctx.Client.PostAsync(
            $"/api/content/pages/{pageId}/attachments/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not match the file's actual format", body);
    }

    [Fact]
    public async Task Upload_HtmlMime_RejectedByAllowlist()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        var html = Encoding.UTF8.GetBytes("<html><body></body></html>");
        using var content = BuildMultipart(html, "text/html", "evil.html");

        var response = await ctx.Client.PostAsync(
            $"/api/content/pages/{pageId}/attachments/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not allowed", body);
    }

    [Fact]
    public async Task Upload_SvgMime_RejectedByAllowlist()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>1</script></svg>");
        using var content = BuildMultipart(svg, "image/svg+xml", "evil.svg");

        var response = await ctx.Client.PostAsync(
            $"/api/content/pages/{pageId}/attachments/", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Download_SafeImage_SetsHardeningHeaders()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        using var upload = BuildMultipart(BuildPngBytes(), "image/png", "image.png");
        var uploadResponse = await ctx.Client.PostAsync(
            $"/api/content/pages/{pageId}/attachments/", upload);
        uploadResponse.EnsureSuccessStatusCode();
        var attachmentId = await ReadAttachmentIdAsync(uploadResponse);

        var download = await ctx.Client.GetAsync(
            $"/api/content/attachments/{attachmentId}/download");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        Assert.Equal("nosniff",
            Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));

        var csp = Assert.Single(download.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("sandbox", csp);
        Assert.Contains("frame-ancestors 'none'", csp);

        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Download_StoredDangerousContentType_ServedAsOctetStream()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.PrimeAuthAsync();
        var pageId = await ctx.SeedPageAsync();

        // Simulate a row that pre-dates the strict upload sniff: write
        // bytes through the store directly, then insert a metadata row
        // with a dangerous Content-Type so we can confirm the download
        // path rewrites it to application/octet-stream.
        var (attachmentId, _) = await ctx.SeedRawAttachmentAsync(
            pageId,
            contentType: "text/html",
            fileName: "legacy.html",
            bytes: Encoding.UTF8.GetBytes("<script>alert(1)</script>"));

        var download = await ctx.Client.GetAsync(
            $"/api/content/attachments/{attachmentId}/download");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/octet-stream",
            download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff",
            Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("attachment",
            download.Content.Headers.ContentDisposition?.DispositionType);
    }

    private static async Task<Guid> ReadAttachmentIdAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildMultipart(
        byte[] bytes, string contentType, string fileName)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    // Minimal valid PNG byte sequence: signature + IHDR + IDAT (empty
    // deflate stream) + IEND. Length doesn't need to decode to a real
    // image — the sniffer only inspects the leading signature.
    private static byte[] BuildPngBytes()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // signature
            0x00, 0x00, 0x00, 0x0D, // IHDR length
            0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00,
            0x90, 0x77, 0x53, 0xDE,
            0x00, 0x00, 0x00, 0x00, // IEND length
            0x49, 0x45, 0x4E, 0x44,
            0xAE, 0x42, 0x60, 0x82
        ];
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;
        private readonly string _attachmentRoot;

        public HttpClient Client { get; }

        private TestContext(
            AutoNateWebApplicationFactory factory, HttpClient client, string attachmentRoot)
        {
            _factory = factory;
            Client = client;
            _attachmentRoot = attachmentRoot;
        }

        public static async Task<TestContext> CreateAsync()
        {
            var attachmentRoot = Path.Combine(
                Path.GetTempPath(),
                "autonate-attachment-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attachmentRoot);

            var factory = await AutoNateWebApplicationFactory.CreateAsync(
                new Dictionary<string, string?>
                {
                    // Schema initializer grants every existing local_user the
                    // built-in SuperAdmin role. Without this the admin user
                    // can't pass the in-handler ContentAuthorizer check on
                    // GET /download and the upload's RequirePermission filter.
                    ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true",
                    ["ContentAttachments:RootPath"] = attachmentRoot
                });

            return new TestContext(factory, factory.CreateClient(), attachmentRoot);
        }

        // The dev auto-login middleware skips POSTs, so any test that
        // uploads needs to land an authenticated cookie first via a GET.
        public async Task PrimeAuthAsync()
        {
            (await Client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        }

        public async Task<Guid> SeedPageAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var tree = sp.GetRequiredService<IContentTreeService>();

            Guid actorId;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                actorId = await db.LocalUsers.AsNoTracking()
                    .Where(u => u.Username == "admin")
                    .Select(u => u.UserId)
                    .FirstAsync();
            }

            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "attachment-tests",
                DeletionsLocked = false,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            var cabinet = new Cabinet
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "cab",
                IsArchived = false,
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            var notebook = new Notebook
            {
                Id = Guid.NewGuid(),
                CabinetId = cabinet.Id,
                Name = "nb",
                IsArchived = false,
                SortOrder = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            var page = new Page
            {
                Id = Guid.NewGuid(),
                NotebookId = notebook.Id,
                ParentPageId = null,
                Title = "p",
                BodyJsonb = "{}",
                CurrentVersionNumber = 1,
                SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Cabinets.Add(cabinet);
                db.Notebooks.Add(notebook);
                db.Pages.Add(page);
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

            return page.Id;
        }

        public async Task<(Guid AttachmentId, string StorageKey)> SeedRawAttachmentAsync(
            Guid pageId, string contentType, string fileName, byte[] bytes)
        {
            using var scope = _factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var store = sp.GetRequiredService<IContentAttachmentStore>();

            Guid projectId;
            Guid actorId;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                projectId = await db.ContentAncestors.AsNoTracking()
                    .Where(ca => ca.DescendantKind == ContentKinds.Page
                                 && ca.DescendantId == pageId
                                 && ca.AncestorKind == ContentKinds.Project)
                    .Select(ca => ca.AncestorId)
                    .FirstAsync();
                actorId = await db.LocalUsers.AsNoTracking()
                    .Where(u => u.Username == "admin")
                    .Select(u => u.UserId)
                    .FirstAsync();
            }

            var attachmentId = Guid.NewGuid();
            await using var ms = new MemoryStream(bytes, writable: false);
            var storageKey = await store.WriteAsync(projectId, attachmentId, ms, default);

            var now = DateTime.UtcNow;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.PageAttachments.Add(new PageAttachment
                {
                    Id = attachmentId,
                    PageId = pageId,
                    FileName = fileName,
                    ContentType = contentType,
                    ByteSize = bytes.LongLength,
                    Sha256Hex = new string('0', 64),
                    StorageKey = storageKey,
                    IsArchived = false,
                    CreatedAtUtc = now,
                    CreatedBy = actorId,
                    UpdatedAtUtc = now,
                    UpdatedBy = actorId
                });
                await db.SaveChangesAsync();
            }

            return (attachmentId, storageKey);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
            try
            {
                if (Directory.Exists(_attachmentRoot))
                {
                    Directory.Delete(_attachmentRoot, recursive: true);
                }
            }
            catch (IOException) { /* best effort */ }
        }
    }
}
