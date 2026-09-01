using System.Net;
using System.Net.Http.Json;
using System.Text;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.DataStores;
using AutoNate.Web.Services.DataStores.File;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Datasets.Files;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// #82: POST /api/datasets/preview-file-source resolves a parser out of
// DatasetFileParserRegistry by caller-supplied kind and runs it over
// caller-supplied file bytes. Nothing tested the dispatch, the unknown-kind
// path, the parser's declared limits, or the gate — so this file walks the
// whole surface, including what the endpoint currently does with input it
// does not like.
//
// Fixtures are built byte-by-byte in each test and seeded through
// IFileDataStoreService into a throwaway Data:Root, so no binary artifacts
// are committed and nothing survives the test.
[Trait("Category", "Integration")]
public sealed class DatasetPreviewFileSourceTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- dispatch per supported parser kind ------------------------------------

    [Fact]
    public async Task PreviewFileSource_CsvFileScope_ReturnsInferredColumnSchema()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "sales.csv", Utf8(
                "id,Full Name,score,active\n" +
                "1,alpha,9.5,true\n" +
                "2,beta,3.1,false\n"));
            var client = await SignedInClientAsync(factory);

            var resp = await PreviewAsync(client, storeId, "file", "/sales.csv", CsvFileParser.KindName);

            resp.EnsureSuccessStatusCode();
            var columns = await ColumnsOfAsync(resp);
            Assert.Collection(columns,
                c => { Assert.Equal("id", c.Name); Assert.Equal("bigint", c.PostgresType); },
                c => { Assert.Equal("full_name", c.Name); Assert.Equal("text", c.PostgresType); },
                c => { Assert.Equal("score", c.Name); Assert.Equal("double precision", c.PostgresType); },
                c => { Assert.Equal("active", c.Name); Assert.Equal("boolean", c.PostgresType); });
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // parserOptions is the only caller-controlled knob that reaches CsvHelper's
    // configuration, so both documented options have to demonstrably change the
    // schema the SPA is about to lock in.
    [Fact]
    public async Task PreviewFileSource_CsvParserOptions_ChangeDelimiterAndHeaderHandling()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "semi.csv", Utf8("a;b;c\n1;2;3\n"));
            var client = await SignedInClientAsync(factory);

            // Default delimiter: the whole line is one column, so the option is
            // not being silently inferred from the content.
            var defaults = await PreviewAsync(client, storeId, "file", "/semi.csv", CsvFileParser.KindName);
            defaults.EnsureSuccessStatusCode();
            Assert.Equal("a_b_c", Assert.Single(await ColumnsOfAsync(defaults)).Name);

            var semicolon = await PreviewAsync(
                client, storeId, "file", "/semi.csv", CsvFileParser.KindName,
                new Dictionary<string, string> { ["delimiter"] = ";" });
            semicolon.EnsureSuccessStatusCode();
            Assert.Equal(
                new[] { "a", "b", "c" },
                (await ColumnsOfAsync(semicolon)).Select(c => c.Name).ToArray());

            // hasHeader=false: the first line becomes data and the columns get
            // positional names.
            var headerless = await PreviewAsync(
                client, storeId, "file", "/semi.csv", CsvFileParser.KindName,
                new Dictionary<string, string> { ["delimiter"] = ";", ["hasHeader"] = "false" });
            headerless.EnsureSuccessStatusCode();
            var positional = await ColumnsOfAsync(headerless);
            Assert.Equal(new[] { "col_1", "col_2", "col_3" }, positional.Select(c => c.Name).ToArray());
            Assert.All(positional, c => Assert.Equal("bigint", c.PostgresType));
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // The raw parser's schema is constant by design — it never inspects the
    // bytes — so a JSON file previews as the same single `content` column.
    [Fact]
    public async Task PreviewFileSource_RawParser_ReturnsSingleContentColumn()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "blob.json", Utf8("{\"a\":1,\"b\":[2,3]}"));
            var client = await SignedInClientAsync(factory);

            var resp = await PreviewAsync(client, storeId, "file", "/blob.json", RawFileParser.KindName);

            resp.EnsureSuccessStatusCode();
            var column = Assert.Single(await ColumnsOfAsync(resp));
            Assert.Equal(RawFileParser.ContentColumnName, column.Name);
            Assert.Equal("text", column.PostgresType);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // Folder scope previews the alphabetically-first non-".keep" file. Seeding
    // b.csv first proves the pick is ordered, not insertion-ordered — the
    // dataset's locked schema depends on which file gets sampled.
    [Fact]
    public async Task PreviewFileSource_FolderScope_SamplesAlphabeticallyFirstFile()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/multi", "b.csv", Utf8("zulu,yankee\n1,2\n"));
            await SeedFileAsync(factory, storeId, "/multi", "a.csv", Utf8("alpha,beta\n1,2\n"));
            var client = await SignedInClientAsync(factory);

            var resp = await PreviewAsync(client, storeId, "folder", "/multi", CsvFileParser.KindName);

            resp.EnsureSuccessStatusCode();
            Assert.Equal(
                new[] { "alpha", "beta" },
                (await ColumnsOfAsync(resp)).Select(c => c.Name).ToArray());
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- rejected input --------------------------------------------------------

    // An unsupported file type is expressed as an unregistered parser kind, and
    // the refusal has to name what IS registered so the SPA can recover.
    [Fact]
    public async Task PreviewFileSource_UnknownParserKind_ReturnsBadRequestNamingRegisteredKinds()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "book.xlsx", Utf8("id,name\n1,alpha\n"));
            var client = await SignedInClientAsync(factory);

            var unknown = await PreviewAsync(client, storeId, "file", "/book.xlsx", "xlsx");

            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            var body = await unknown.Content.ReadAsStringAsync();
            Assert.Contains("No dataset file parser is registered", body, StringComparison.Ordinal);
            Assert.Contains(CsvFileParser.KindName, body, StringComparison.Ordinal);
            Assert.Contains(RawFileParser.KindName, body, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);

            var blank = await PreviewAsync(client, storeId, "file", "/book.xlsx", "   ");
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            Assert.Contains("parserKind is required", await blank.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task PreviewFileSource_UnknownScopeOrDataStore_ReturnsBadRequest()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "rows.csv", Utf8("id,name\n1,alpha\n"));
            var client = await SignedInClientAsync(factory);

            var badScope = await PreviewAsync(client, storeId, "glob", "/rows.csv", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);
            Assert.Contains("Unknown scopeKind", await badScope.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var noPath = await PreviewAsync(client, storeId, "file", "  ", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.BadRequest, noPath.StatusCode);
            Assert.Contains("scopePath is required", await noPath.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var noStore = await PreviewAsync(client, Guid.NewGuid(), "file", "/rows.csv", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.BadRequest, noStore.StatusCode);
            Assert.Contains("not found", await noStore.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public async Task PreviewFileSource_MissingFileOrEmptyFolder_ReturnsNotFound()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFolderAsync(factory, storeId, "/empty");
            var client = await SignedInClientAsync(factory);

            var missingFile = await PreviewAsync(client, storeId, "file", "/nope.csv", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.NotFound, missingFile.StatusCode);
            Assert.Contains("/nope.csv", await missingFile.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // The folder exists but holds only the ".keep" placeholder, which
            // the folder branch filters out — so there is nothing to sample.
            var emptyFolder = await PreviewAsync(client, storeId, "folder", "/empty", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.NotFound, emptyFolder.StatusCode);
            Assert.Contains("no files to sample", await emptyFolder.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- declared limits -------------------------------------------------------

    // CsvFileParser declares MaxColumnCount = 256 and SampleSize = 200. Both are
    // the only backstops between an untrusted file and the schema the SPA locks
    // into a dataset, so both are pinned here.
    [Fact]
    public async Task PreviewFileSource_EnforcesDeclaredColumnAndSampleLimits()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);

            var wideHeader = string.Join(",", Enumerable.Range(1, 257).Select(i => "c" + i));
            var wideRow = string.Join(",", Enumerable.Range(1, 257).Select(_ => "1"));
            await SeedFileAsync(factory, storeId, "/", "wide.csv", Utf8(wideHeader + "\n" + wideRow + "\n"));

            // 200 integer rows, then a text row that falls outside the sample.
            var beyondSample = new StringBuilder("v\n");
            for (var i = 1; i <= 200; i++) beyondSample.Append(i).Append('\n');
            beyondSample.Append("abc\n");
            await SeedFileAsync(factory, storeId, "/", "beyond-sample.csv", Utf8(beyondSample.ToString()));

            // Control: the same text value inside the sample does flip the type,
            // so the assertion above is about the cap and not about parsing.
            await SeedFileAsync(factory, storeId, "/", "within-sample.csv", Utf8("v\n1\n2\nabc\n"));

            var client = await SignedInClientAsync(factory);

            var tooWide = await PreviewAsync(client, storeId, "file", "/wide.csv", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
            var wideBody = await tooWide.Content.ReadAsStringAsync();
            Assert.Contains("out of range (1..256)", wideBody, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", wideBody, StringComparison.OrdinalIgnoreCase);

            var capped = await PreviewAsync(client, storeId, "file", "/beyond-sample.csv", CsvFileParser.KindName);
            capped.EnsureSuccessStatusCode();
            Assert.Equal("bigint", Assert.Single(await ColumnsOfAsync(capped)).PostgresType);

            var control = await PreviewAsync(client, storeId, "file", "/within-sample.csv", CsvFileParser.KindName);
            control.EnsureSuccessStatusCode();
            Assert.Equal("text", Assert.Single(await ColumnsOfAsync(control)).PostgresType);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- malformed / mistyped input (current behaviour, see report) ------------

    // FINDING, asserted as-is rather than as it arguably should behave: the
    // endpoint does no content sniffing and CsvFileParser is configured with
    // BadDataFound = null, so neither binary bytes handed to the CSV parser nor
    // a CSV truncated mid-quoted-field is rejected. Both come back 200 with a
    // schema the caller is then invited to lock into a dataset. If either of
    // these ever starts returning 4xx, that is an intentional hardening and
    // this test should be updated, not deleted.
    [Fact]
    public async Task PreviewFileSource_BinaryOrTruncatedFile_IsAcceptedWithoutSniffing()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);

            // Parquet-shaped bytes deliberately free of commas, quotes and
            // newlines so the decoded text is one deterministic field.
            var parquetish = new byte[]
            {
                0x50, 0x41, 0x52, 0x31, 0x01, 0x02, 0x03, 0x04, 0x50, 0x41, 0x52, 0x31
            };
            await SeedFileAsync(factory, storeId, "/", "data.parquet", parquetish);
            await SeedFileAsync(factory, storeId, "/", "truncated.csv", Utf8("id,name\n1,\"alpha"));

            var client = await SignedInClientAsync(factory);

            var binary = await PreviewAsync(client, storeId, "file", "/data.parquet", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.OK, binary.StatusCode);
            var binaryColumn = Assert.Single(await ColumnsOfAsync(binary));
            Assert.Equal("par1____par1", binaryColumn.Name);
            Assert.Equal("text", binaryColumn.PostgresType);

            var truncated = await PreviewAsync(client, storeId, "file", "/truncated.csv", CsvFileParser.KindName);
            Assert.Equal(HttpStatusCode.OK, truncated.StatusCode);
            Assert.Equal(
                new[] { "id", "name" },
                (await ColumnsOfAsync(truncated)).Select(c => c.Name).ToArray());
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // A folder's ".keep" placeholder row carries an empty storage_key, and the
    // endpoint downloads the file before handing it to a parser.
    // FileDataStoreService.ResolveAbsolutePath("") resolves to the datastores
    // root DIRECTORY, so File.OpenRead threw and — with no exception-handling
    // middleware — the caller got a 500 carrying the exception text and stack.
    // The folder branch already filtered ".keep" out; the file branch now does
    // too, so it is simply not found (#184).
    [Fact]
    public async Task PreviewFileSource_FolderPlaceholderKeepFile_ReturnsNotFound()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(Config(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFolderAsync(factory, storeId, "/empty");
            var client = await SignedInClientAsync(factory);

            var resp = await PreviewAsync(client, storeId, "file", "/empty/.keep", RawFileParser.KindName);

            // #184: a clean 404, and nothing about the server in the body.
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
            Assert.DoesNotContain("at AutoNate", body, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- gate ------------------------------------------------------------------

    [Fact]
    public async Task PreviewFileSource_WithoutDatasetCreateGrant_IsForbidden()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "sales.csv", Utf8("id,secret_column\n1,alpha\n"));
            // Everything except the gate under test: dataset:view must not
            // stand in for dataset:create on a parser-dispatch endpoint.
            await GrantAsync(factory, Actions.View, "/dataset/*");
            await GrantAsync(factory, Actions.View, "/datastore/*");
            var client = await SignedInClientAsync(factory);

            var resp = await PreviewAsync(client, storeId, "file", "/sales.csv", CsvFileParser.KindName);

            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.DoesNotContain("secret_column", await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // #183: dataset:create alone must not read a datastore's contents.
    //
    // The route gate is (Dataset, Create) and that used to be the only check,
    // so a caller with no datastore grants — one who gets an empty list from
    // GET /api/datastores — could still name any store in the request body
    // and read back its column names. One feature's permission bypassed
    // another's. The handler now authorizes (DataStore, View) against the
    // store being read, the same pair GET /api/datastores/{id}/files uses.
    [Fact]
    public async Task PreviewFileSource_WithoutDataStoreViewGrant_IsForbiddenAndLeaksNoColumns()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "sales.csv", Utf8("id,secret_column\n1,alpha\n"));
            await GrantAsync(factory, Actions.Create, "/dataset/*");
            var client = await SignedInClientAsync(factory);

            // No datastore grant at all: prove it, so the refusal below is
            // unambiguously about the datastore and not about the dataset.
            var storeList = await client.GetAsync("/api/datastores");
            storeList.EnsureSuccessStatusCode();
            Assert.Empty(await storeList.Content.ReadFromJsonAsync<List<StoreRow>>() ?? new List<StoreRow>());

            var resp = await PreviewAsync(client, storeId, "file", "/sales.csv", CsvFileParser.KindName);

            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            // The column name is the payload worth protecting here.
            Assert.DoesNotContain(
                "secret_column",
                await resp.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // The positive half: with View on the store, the preview works — so the
    // fix added a check rather than breaking the feature.
    [Fact]
    public async Task PreviewFileSource_WithDataStoreViewGrant_ReturnsColumns()
    {
        var dataRoot = NewDataRoot();
        try
        {
            await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig(dataRoot));
            var storeId = await SeedStoreAsync(factory);
            await SeedFileAsync(factory, storeId, "/", "sales.csv", Utf8("id,secret_column\n1,alpha\n"));
            await GrantAsync(factory, Actions.Create, "/dataset/*");
            await GrantAsync(factory, Actions.View, "/datastore/*");
            var client = await SignedInClientAsync(factory);

            var resp = await PreviewAsync(client, storeId, "file", "/sales.csv", CsvFileParser.KindName);

            resp.EnsureSuccessStatusCode();
            Assert.Equal(
                new[] { "id", "secret_column" },
                (await ColumnsOfAsync(resp)).Select(c => c.Name).ToArray());
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    // ---- helpers ---------------------------------------------------------------

    private static byte[] Utf8(string content) => Encoding.UTF8.GetBytes(content);

    private static string NewDataRoot() =>
        Path.Combine(Path.GetTempPath(), "autonate-dataset-preview-tests-" + Guid.NewGuid().ToString("N"));

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

    private static async Task<Guid> SeedStoreAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var stores = scope.ServiceProvider.GetRequiredService<IDataStoreStore>();
        var store = await stores.CreateAsync(
            new CreateDataStoreInput(
                "store-" + Guid.NewGuid().ToString("N")[..8], null, DataStoreKind.FileType),
            AdminUserId);
        return store.Id;
    }

    private static async Task SeedFileAsync(
        AutoNateWebApplicationFactory factory, Guid storeId, string folder, string filename, byte[] bytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var files = scope.ServiceProvider.GetRequiredService<IFileDataStoreService>();
        using var content = new MemoryStream(bytes);
        await files.UploadAsync(storeId, folder, filename, "application/octet-stream", content, AdminUserId);
    }

    private static async Task SeedFolderAsync(
        AutoNateWebApplicationFactory factory, Guid storeId, string folder)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var files = scope.ServiceProvider.GetRequiredService<IFileDataStoreService>();
        await files.CreateFolderAsync(storeId, folder);
    }

    private static Task<HttpResponseMessage> PreviewAsync(
        HttpClient client,
        Guid storeId,
        string scopeKind,
        string scopePath,
        string parserKind,
        Dictionary<string, string>? options = null) =>
        client.PostAsJsonAsync("/api/datasets/preview-file-source", new
        {
            dataStoreId = storeId,
            scopeKind,
            scopePath,
            parserKind,
            parserOptions = options
        });

    private static async Task<IReadOnlyList<DatasetColumn>> ColumnsOfAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadFromJsonAsync<PreviewFileSourceResponse>()
            ?? throw new InvalidOperationException("Preview response was empty.");
        return body.Columns;
    }

    private sealed record StoreRow(Guid Id, string Name);
}
