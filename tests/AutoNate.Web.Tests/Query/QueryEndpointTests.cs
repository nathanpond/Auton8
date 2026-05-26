using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AutoNate.Web.Tests.Query;

[Trait("Category", "Integration")]
public sealed class QueryEndpointTests
{
    private sealed record ExecuteQueryResponseDto(
        List<ColumnDto> Columns,
        List<Dictionary<string, JsonElement>> Rows,
        long TotalCount,
        bool Truncated,
        long DurationMs);

    private sealed record ColumnDto(string Name, string DataType);

    [Fact]
    public async Task FromRecords_OnEmpty_ReturnsEmptyRows_WithSchemaColumns()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        // GET probe primes dev auto-login.
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new { query = "FROM Records" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Columns);
        Assert.Empty(body.Rows);
        Assert.Contains(body.Columns, c => c.Name == "RecordType");
        Assert.Contains(body.Columns, c => c.Name == "CreatedDate");
    }

    [Fact]
    public async Task BareWhere_DefaultsToRecordsEntity_And_FriendlyError_OnUnknownRecordType()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "RecordType = \"NonexistentTypeXYZ\""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var errors = doc.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("NonexistentTypeXYZ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownField_Returns_Friendly_Error()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Records WHERE NoSuchField = 1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var errors = doc.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("NoSuchField", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NumExecutions_On_Workflows_Resolves_Against_Execution_Cache()
    {
        // Replaces the prior "pending cache" gate. NUMEXECUTIONS / LASTEXECUTED
        // are now resolved from workflow_execution_cache by the projection
        // framework; with no cache rows, NUMEXECUTIONS() returns 0 for every
        // workflow, so a predicate of `> 0` should return an empty result set
        // rather than a validation error.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Workflows WHERE NUMEXECUTIONS() > 0"
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var rows = doc.GetProperty("rows");
        Assert.Equal(0, rows.GetArrayLength());
    }

    [Fact]
    public async Task FromWorkflows_ReturnsConfiguredColumns()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new { query = "FROM Workflows" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        Assert.Contains(body!.Columns, c => c.Name == "ModelName");
        Assert.Contains(body.Columns, c => c.Name == "Published");
        Assert.Contains(body.Columns, c => c.Name == "Version");
        Assert.Contains(body.Columns, c => c.Name == "CreatedDate");
    }

    [Fact]
    public async Task UnknownEntity_Returns_Friendly_Error()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new { query = "FROM Banana" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var errors = doc.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("Banana", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FromRecords_Columns_AreCaseInsensitive()
    {
        // Regression: lowercase/mixed-case names like `key`, `recordtype` used
        // to throw an unhandled InvalidOperationException out of
        // SystemColumns.ToSqlExpr and surface as a 500.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Records COLUMNS(key, name, recordtype)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ExecuteQueryResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.Columns.Count);
    }

    [Fact]
    public async Task FromRecords_Where_FieldName_IsCaseInsensitive()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/query", new
        {
            query = "FROM Records WHERE isarchived = false COLUMNS(key)"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task QueryMenu_IsSeeded_OnFreshInstall()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var menuResp = await client.GetAsync("/api/menus/main");
        Assert.Equal(HttpStatusCode.OK, menuResp.StatusCode);
        var menu = await menuResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = menu.GetProperty("items").EnumerateArray();
        var queryItem = items.FirstOrDefault(i =>
            i.TryGetProperty("displayName", out var dn) && dn.GetString() == "Query");
        Assert.True(queryItem.ValueKind != JsonValueKind.Undefined,
            "Top-level 'Query' menu item should be seeded into the main menu.");
    }
}
