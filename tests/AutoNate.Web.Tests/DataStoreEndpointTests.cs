using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.DataStores.Sql;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// archived-81: the file sub-surface of /api/datastores — multipart upload, single-file
// copy, recursive folder copy and the CSV table preview — had no endpoint test,
// and EntityKinds.DataStore appeared in no *EnforcementTests.cs at all. Nothing
// proved that the one storage kind holding arbitrary user bytes actually
// refuses a caller who holds a store id but no grant.
//
// Every test pins the store's file bytes to a throwaway Data:Root so the
// round-trip assertions read the real on-disk artifacts and the directory can
// be swept afterwards.
[Trait("Category", "Integration")]
public sealed class DataStoreEndpointTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- upload / download round trip -----------------------------------------

    [Fact]
    public async Task PostFile_WithMultipartUpload_RoundTripsBytesAndMetadata()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            const string payload = "col_a,col_b\n1,2\n";
            var uploadResp = await UploadAsync(
                client, storeId, "/reports", "quarterly.csv", "text/csv", Encoding.UTF8.GetBytes(payload));

            Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);
            var uploaded = await ReadFileAsync(uploadResp);
            Assert.NotEqual(Guid.Empty, uploaded.Id);
            Assert.Equal(storeId, uploaded.DataStoreId);
            Assert.Equal("/reports", uploaded.FolderPath);
            Assert.Equal("quarterly.csv", uploaded.Filename);
            Assert.Equal("text/csv", uploaded.ContentType);
            Assert.Equal((long)Encoding.UTF8.GetByteCount(payload), uploaded.SizeBytes);
            Assert.Equal(AdminUserId, uploaded.UploadedBy);

            // Read-back #1: the file is indexed under the folder it was posted
            // to, not at the root.
            var folderListing = await ListAsync(client, storeId, "/reports");
            var entry = Assert.Single(folderListing.Files);
            Assert.Equal(uploaded.Id, entry.Id);
            Assert.Equal("quarterly.csv", entry.Filename);
            Assert.Equal(uploaded.SizeBytes, entry.SizeBytes);

            // Read-back #2: folders are synthesized from the file rows, so the
            // root listing must surface /reports as a child folder and no files.
            var rootListing = await ListAsync(client, storeId, "/");
            Assert.Empty(rootListing.Files);
            Assert.Contains(rootListing.Folders, f => f.FolderPath == "/reports");

            // Read-back #3: the stored bytes come back byte-identical, as an
            // attachment, with sniffing disabled (archived-65).
            var download = await client.GetAsync($"/api/datastores/{storeId}/files/{uploaded.Id}");
            download.EnsureSuccessStatusCode();
            Assert.Equal(payload, await download.Content.ReadAsStringAsync());
            Assert.Equal("text/csv", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal(
                "quarterly.csv",
                download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // The uploader controls the multipart Content-Type, so same-origin
    // text/html bytes must not be echoed back as text/html (archived-65).
    [Fact]
    public async Task GetFile_WithHtmlContentType_DowngradesResponseTypeAndSetsNoSniff()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            const string html = "<script>alert(1)</script>";
            var uploadResp = await UploadAsync(
                client, storeId, "/", "payload.html", "text/html", Encoding.UTF8.GetBytes(html));
            uploadResp.EnsureSuccessStatusCode();
            var uploaded = await ReadFileAsync(uploadResp);

            // The declared type is preserved in metadata — the downgrade is a
            // response-time decision, not a storage-time rewrite.
            Assert.Equal("text/html", uploaded.ContentType);

            var download = await client.GetAsync($"/api/datastores/{storeId}/files/{uploaded.Id}");
            download.EnsureSuccessStatusCode();
            Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(html, await download.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // Folder paths and filenames both land on disk-adjacent code paths, so a
    // traversal attempt has to be refused before any row or byte is written.
    [Fact]
    public async Task PostFile_WithInvalidFolderOrFilename_IsRejectedAndStoresNothing()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);
            var bytes = Encoding.UTF8.GetBytes("owned");

            var traversal = await UploadAsync(client, storeId, "/../escape", "ok.txt", "text/plain", bytes);
            Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
            Assert.Contains("Invalid folder path", await traversal.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var separatorInName = await UploadAsync(client, storeId, "/", "sub/evil.txt", "text/plain", bytes);
            Assert.Equal(HttpStatusCode.BadRequest, separatorInName.StatusCode);
            Assert.Contains("path separators", await separatorInName.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var noFile = new MultipartFormDataContent { { new StringContent("/"), "folder" } };
            var missingPart = await client.PostAsync($"/api/datastores/{storeId}/files", noFile);
            Assert.Equal(HttpStatusCode.BadRequest, missingPart.StatusCode);
            Assert.Contains("form-file", await missingPart.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // Nothing above may have created a file row or a synthesized folder.
            var listing = await ListAsync(client, storeId, "/");
            Assert.Empty(listing.Files);
            Assert.Empty(listing.Folders);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // The unique index is on (datastore, folder, LOWER(filename)); the second
    // upload must not overwrite the first one's bytes on its way to the 409.
    [Fact]
    public async Task PostFile_WithDuplicateFilename_ReturnsConflictAndKeepsOriginalBytes()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            var first = await UploadAsync(client, storeId, "/", "a.txt", "text/plain", Encoding.UTF8.GetBytes("first"));
            first.EnsureSuccessStatusCode();
            var original = await ReadFileAsync(first);

            var second = await UploadAsync(client, storeId, "/", "A.TXT", "text/plain", Encoding.UTF8.GetBytes("second"));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

            var listing = await ListAsync(client, storeId, "/");
            Assert.Equal(original.Id, Assert.Single(listing.Files).Id);

            var download = await client.GetAsync($"/api/datastores/{storeId}/files/{original.Id}");
            download.EnsureSuccessStatusCode();
            Assert.Equal("first", await download.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- copy -----------------------------------------------------------------

    // A copy must duplicate the bytes, not alias the source's storage key —
    // otherwise deleting the source would take the copy's content with it.
    [Fact]
    public async Task PostFileCopy_ToAnotherFolder_CreatesIndependentCopy()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            const string payload = "the-payload";
            var uploadResp = await UploadAsync(
                client, storeId, "/src", "data.txt", "text/plain", Encoding.UTF8.GetBytes(payload));
            uploadResp.EnsureSuccessStatusCode();
            var source = await ReadFileAsync(uploadResp);

            var copyResp = await client.PostAsJsonAsync(
                $"/api/datastores/{storeId}/files/{source.Id}/copy",
                new CopyFileRequest(TargetFolderPath: "/dst", NewFilename: "data-copy.txt"));

            Assert.Equal(HttpStatusCode.Created, copyResp.StatusCode);
            var copy = await ReadFileAsync(copyResp);
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal("/dst", copy.FolderPath);
            Assert.Equal("data-copy.txt", copy.Filename);
            Assert.Equal(source.SizeBytes, copy.SizeBytes);
            Assert.Equal(source.ContentType, copy.ContentType);
            Assert.NotEqual(source.StorageKey, copy.StorageKey);

            // Delete the source, then read the copy: independent bytes.
            var delete = await client.DeleteAsync($"/api/datastores/{storeId}/files/{source.Id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

            var download = await client.GetAsync($"/api/datastores/{storeId}/files/{copy.Id}");
            download.EnsureSuccessStatusCode();
            Assert.Equal(payload, await download.Content.ReadAsStringAsync());

            // Copying onto an existing (folder, filename) pair is a conflict,
            // not a silent overwrite.
            var conflict = await client.PostAsJsonAsync(
                $"/api/datastores/{storeId}/files/{copy.Id}/copy",
                new CopyFileRequest(TargetFolderPath: "/dst", NewFilename: null));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Single((await ListAsync(client, storeId, "/dst")).Files);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task PostFolderCopy_CopiesEveryFileUnderPrefixAndLeavesSource()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            (await UploadAsync(client, storeId, "/src", "a.txt", "text/plain", Encoding.UTF8.GetBytes("A")))
                .EnsureSuccessStatusCode();
            (await UploadAsync(client, storeId, "/src/sub", "b.txt", "text/plain", Encoding.UTF8.GetBytes("B")))
                .EnsureSuccessStatusCode();

            var copyResp = await client.PostAsJsonAsync(
                $"/api/datastores/{storeId}/folders/copy",
                new CopyFolderRequest(SourcePath: "/src", TargetPath: "/dst"));
            Assert.Equal(HttpStatusCode.NoContent, copyResp.StatusCode);

            // Recursion: the nested file has to land under the mirrored path.
            var dst = await ListAsync(client, storeId, "/dst");
            Assert.Equal("a.txt", Assert.Single(dst.Files).Filename);
            Assert.Contains(dst.Folders, f => f.FolderPath == "/dst/sub");

            var dstSub = await ListAsync(client, storeId, "/dst/sub");
            var nested = Assert.Single(dstSub.Files);
            Assert.Equal("b.txt", nested.Filename);

            var nestedDownload = await client.GetAsync($"/api/datastores/{storeId}/files/{nested.Id}");
            nestedDownload.EnsureSuccessStatusCode();
            Assert.Equal("B", await nestedDownload.Content.ReadAsStringAsync());

            // Copy, not move: the source tree survives.
            Assert.Equal("a.txt", Assert.Single((await ListAsync(client, storeId, "/src")).Files).Filename);
            Assert.Equal("b.txt", Assert.Single((await ListAsync(client, storeId, "/src/sub")).Files).Filename);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // Copying a folder into itself would recurse forever over the prefix scan,
    // and copying a folder that holds nothing is a client mistake, not a 500.
    [Fact]
    public async Task PostFolderCopy_IntoOwnDescendantOrFromMissingSource_IsRejected()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            (await UploadAsync(client, storeId, "/src", "a.txt", "text/plain", Encoding.UTF8.GetBytes("A")))
                .EnsureSuccessStatusCode();

            var descendant = await client.PostAsJsonAsync(
                $"/api/datastores/{storeId}/folders/copy",
                new CopyFolderRequest(SourcePath: "/src", TargetPath: "/src/nested"));
            Assert.Equal(HttpStatusCode.BadRequest, descendant.StatusCode);
            Assert.Contains("own descendant", await descendant.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var missing = await client.PostAsJsonAsync(
                $"/api/datastores/{storeId}/folders/copy",
                new CopyFolderRequest(SourcePath: "/nope", TargetPath: "/dst"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            // Neither rejection may have left a partial tree behind.
            var root = await ListAsync(client, storeId, "/");
            Assert.Equal("/src", Assert.Single(root.Folders).FolderPath);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- CSV table preview ----------------------------------------------------

    [Fact]
    public async Task PostTablesPreview_WithCsv_ReturnsInferredColumnsAndSuggestedTableName()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            var resp = await PostTablePreviewAsync(
                client, storeId, "Q1 Report.csv",
                Encoding.UTF8.GetBytes("id,Full Name,score\n1,alpha,9.5\n2,beta,3.1\n"));

            resp.EnsureSuccessStatusCode();
            var preview = await resp.Content.ReadFromJsonAsync<CsvIngestPreview>();
            Assert.NotNull(preview);
            Assert.Equal("q1_report", preview!.SuggestedTableName);
            Assert.Equal(2, preview.SampleRowCount);
            Assert.Collection(preview.Columns,
                c => { Assert.Equal("id", c.Name); Assert.Equal("bigint", c.PostgresType); },
                c => { Assert.Equal("full_name", c.Name); Assert.Equal("text", c.PostgresType); },
                c => { Assert.Equal("score", c.Name); Assert.Equal("double precision", c.PostgresType); });

            using var empty = new MultipartFormDataContent { { new StringContent("x"), "tableName" } };
            var noFile = await client.PostAsync($"/api/datastores/{storeId}/tables/preview", empty);
            Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // CsvIngestor caps a preview at 256 columns; a wider file is a clean 400,
    // never an unbounded CREATE TABLE proposal.
    [Fact]
    public async Task PostTablesPreview_WithTooManyColumns_ReturnsBadRequest()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            var header = string.Join(",", Enumerable.Range(1, 257).Select(i => "c" + i));
            var row = string.Join(",", Enumerable.Range(1, 257).Select(_ => "1"));

            var resp = await PostTablePreviewAsync(
                client, storeId, "wide.csv", Encoding.UTF8.GetBytes(header + "\n" + row + "\n"));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("out of range (1..256)", body, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- enforcement (archived-81: EntityKinds.DataStore had no enforcement test) -----

    // Upload is gated on (DataStore, Edit) per store — a create grant alone,
    // which is what a "can make their own stores" role would carry, must not
    // let the caller push bytes into someone else's store.
    [Fact]
    public async Task PostFile_WithoutDataStoreEditGrant_IsForbidden()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            await GrantAsync(factory, Actions.Create, "/datastore/*");
            await GrantAsync(factory, Actions.View, "/datastore/*");
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            var resp = await UploadAsync(
                client, storeId, "/", "secret.txt", "text/plain", Encoding.UTF8.GetBytes("payload"));

            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Empty((await ListAsync(client, storeId, "/")).Files);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task PostFile_WithDataStoreEditGrant_Succeeds()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            await GrantAsync(factory, Actions.Create, "/datastore/*");
            await GrantAsync(factory, Actions.Edit, "/datastore/*");
            await GrantAsync(factory, Actions.View, "/datastore/*");
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            var resp = await UploadAsync(
                client, storeId, "/", "secret.txt", "text/plain", Encoding.UTF8.GetBytes("payload"));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            Assert.Equal("secret.txt", Assert.Single((await ListAsync(client, storeId, "/")).Files).Filename);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // The exfiltration case from archived-81: holding a file id is not read access.
    // Download is gated on (DataStore, View), which the uploader does not
    // implicitly acquire by having written the file.
    [Fact]
    public async Task GetFile_WithoutDataStoreViewGrant_IsForbiddenAndLeaksNoBytes()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            await GrantAsync(factory, Actions.Create, "/datastore/*");
            await GrantAsync(factory, Actions.Edit, "/datastore/*");
            var client = await SignedInClientAsync(factory);
            var storeId = await CreateFileStoreAsync(client);

            const string secret = "top-secret-datastore-bytes";
            var uploadResp = await UploadAsync(
                client, storeId, "/", "secret.txt", "text/plain", Encoding.UTF8.GetBytes(secret));
            uploadResp.EnsureSuccessStatusCode();
            var uploaded = await ReadFileAsync(uploadResp);

            var download = await client.GetAsync($"/api/datastores/{storeId}/files/{uploaded.Id}");
            Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
            Assert.DoesNotContain(secret, await download.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var listing = await client.GetAsync($"/api/datastores/{storeId}/files?folder=/");
            Assert.Equal(HttpStatusCode.Forbidden, listing.StatusCode);
            Assert.DoesNotContain("secret.txt", await listing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // Copy duplicates bytes, so it is a write: View is not enough. The store
    // and file are seeded through the service so the grant under test is the
    // only one in play.
    [Fact]
    public async Task PostFileCopy_WithoutDataStoreEditGrant_IsForbidden()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            var (storeId, fileId) = await SeedStoreWithFileAsync(factory, "/", "source.txt", Encoding.UTF8.GetBytes("payload"));
            await GrantAsync(factory, Actions.View, "/datastore/*");
            var client = await SignedInClientAsync(factory);

            var resp = await client.PostAsJsonAsync(
                $"/api/datastores/{storeId}/files/{fileId}/copy",
                new CopyFileRequest(TargetFolderPath: "/copies", NewFilename: null));

            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Empty((await ListAsync(client, storeId, "/copies")).Files);
            Assert.Single((await ListAsync(client, storeId, "/")).Files);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // /tables/preview parses a caller-supplied CSV, so it is gated on Edit even
    // though it only reads. A View-only caller must not reach the parser.
    [Fact]
    public async Task PostTablesPreview_WithOnlyDataStoreViewGrant_IsForbidden()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            var (storeId, _) = await SeedStoreWithFileAsync(factory, "/", "seed.txt", Encoding.UTF8.GetBytes("seed"));
            await GrantAsync(factory, Actions.View, "/datastore/*");
            var client = await SignedInClientAsync(factory);

            var resp = await PostTablePreviewAsync(
                client, storeId, "rows.csv", Encoding.UTF8.GetBytes("id,name\n1,alpha\n"));

            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.DoesNotContain("suggestedTableName", await resp.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- helpers ---------------------------------------------------------------

    private static string NewDataRoot() =>
        Path.Combine(Path.GetTempPath(), "autonate-datastore-endpoint-tests-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDataRoot(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException) { /* best effort — the temp dir is disposable. */ }
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<string, string?> Config(string dataRoot) => new()
    {
        ["Data:Root"] = dataRoot
    };

    private static Dictionary<string, string?> EnforceConfig(string dataRoot) => new()
    {
        ["Data:Root"] = dataRoot,
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task<HttpClient> SignedInClientAsync(AutoNateWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        // Dev auto-login skips POSTs, so land the cookie with a GET first.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string action, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, selector, "allow", 0), AdminUserId);
    }

    private static async Task<Guid> CreateFileStoreAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/datastores",
            new CreateDataStoreRequest(
                Name: "store-" + Guid.NewGuid().ToString("N")[..8],
                Description: null,
                Kind: "FileType"));
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<DataStoreIdDto>()
            ?? throw new InvalidOperationException("Create-store response was empty.");
        return dto.Id;
    }

    private static async Task<(Guid StoreId, Guid FileId)> SeedStoreWithFileAsync(
        AutoNateWebApplicationFactory factory, string folder, string filename, byte[] bytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var stores = scope.ServiceProvider.GetRequiredService<IDataStoreStore>();
        var store = await stores.CreateAsync(
            new CreateDataStoreInput(
                "store-" + Guid.NewGuid().ToString("N")[..8], null, DataStoreKind.FileType),
            AdminUserId);
        var files = scope.ServiceProvider.GetRequiredService<IFileDataStoreService>();
        using var content = new MemoryStream(bytes);
        var entity = await files.UploadAsync(store.Id, folder, filename, "text/plain", content, AdminUserId);
        return (store.Id, entity.Id);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid storeId, string folder, string filename, string contentType, byte[] bytes)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(folder), "folder");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", filename);
        return await client.PostAsync($"/api/datastores/{storeId}/files", multipart);
    }

    private static async Task<HttpResponseMessage> PostTablePreviewAsync(
        HttpClient client, Guid storeId, string filename, byte[] bytes)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        multipart.Add(fileContent, "file", filename);
        return await client.PostAsync($"/api/datastores/{storeId}/tables/preview", multipart);
    }

    private static async Task<DataStoreFile> ReadFileAsync(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<DataStoreFile>()
        ?? throw new InvalidOperationException("File metadata response was empty.");

    private static async Task<FileListing> ListAsync(HttpClient client, Guid storeId, string folder)
    {
        var resp = await client.GetAsync(
            $"/api/datastores/{storeId}/files?folder={Uri.EscapeDataString(folder)}");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<FileListing>()
            ?? throw new InvalidOperationException("File listing response was empty.");
    }

    private sealed record DataStoreIdDto(Guid Id);
}
