using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AutoNate.Web.Tests.Query;

[Trait("Category", "Integration")]
public sealed class AqlSchemaEndpointTests
{
    private sealed record SchemaResponseDto(
        List<string> ClauseKeywords,
        List<AggregateDto> GlobalAggregates,
        List<string> WhereFunctions,
        Dictionary<string, List<string>> OperatorsByDataType,
        List<string> RelativeDateUnits,
        List<EntityDto> Entities);

    private sealed record AggregateDto(string Name, bool RequiresArgument);

    private sealed record EntityDto(
        string Name,
        List<ColumnDto> StaticColumns,
        List<string> AllowedWhereFunctions,
        List<RowFunctionDto> RowFunctions,
        bool HasDynamicFields,
        string? RecordTypeFilterField);

    private sealed record RowFunctionDto(
        string Name,
        bool AcceptsArgument,
        string DataType,
        List<string> Arguments);

    private sealed record ColumnDto(string Name, string DataType, bool IsAggregable, bool IsSystem);

    private sealed record EntityContextResponseDto(
        string Entity,
        string? ResolvedRecordType,
        List<ColumnDto> Columns,
        Dictionary<string, ValueCompletionDto> ValueCompletions);

    private sealed record ValueCompletionDto(List<string> Values, bool ClosedSet);

    [Fact]
    public async Task Catalog_ReturnsClauseKeywords_OperatorTable_AndAllEntities()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/aql/schema");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<SchemaResponseDto>();
        Assert.NotNull(body);

        Assert.Contains("FROM", body!.ClauseKeywords);
        Assert.Contains("WHERE", body.ClauseKeywords);
        Assert.Contains("ORDER BY", body.ClauseKeywords);
        Assert.Contains("COLUMNS", body.ClauseKeywords);
        Assert.Contains("GROUP", body.ClauseKeywords);

        Assert.Contains(body.GlobalAggregates, a => a.Name == "COUNT" && !a.RequiresArgument);
        Assert.Contains(body.GlobalAggregates, a => a.Name == "MEDIAN" && a.RequiresArgument);

        // Operator table mirrors AqlValidator.IsOperatorSupported.
        Assert.Equal(new[] { "=", "!=", "~" }, body.OperatorsByDataType["string"]);
        Assert.Equal(new[] { "=", "!=", "<", "<=", ">", ">=" }, body.OperatorsByDataType["number"]);
        Assert.Equal(new[] { "=", "!=" }, body.OperatorsByDataType["bool"]);
        Assert.Equal(new[] { "=", "!=", "<", "<=", ">", ">=" }, body.OperatorsByDataType["date"]);

        // Every registered entity appears in the catalog.
        var byName = body.Entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        Assert.True(byName.ContainsKey("Records"));
        Assert.True(byName.ContainsKey("Flows"));
        Assert.True(byName.ContainsKey("Workflows"));
        Assert.True(byName.ContainsKey("WorkflowExecutions"));
        Assert.True(byName.ContainsKey("Notes"));

        // Only Records advertises dynamic fields.
        Assert.True(byName["Records"].HasDynamicFields);
        Assert.Equal("RecordType", byName["Records"].RecordTypeFilterField);
        Assert.False(byName["Flows"].HasDynamicFields);
        Assert.Null(byName["Flows"].RecordTypeFilterField);

        // Workflows declares its row functions.
        var wf = byName["Workflows"];
        Assert.Contains(wf.RowFunctions, f => f.Name == "NUMNODES");
        Assert.Contains(wf.RowFunctions, f => f.Name == "NUMEXECUTIONS");
        Assert.Contains(wf.RowFunctions, f => f.Name == "LASTEXECUTED");

        // Flows.CURRENTSTEP publishes its closed-set argument vocabulary.
        var flows = byName["Flows"];
        var currentStep = flows.RowFunctions.Single(f => f.Name == "CURRENTSTEP");
        Assert.True(currentStep.AcceptsArgument);
        Assert.Equal(
            new[] { "Name", "Assignee", "ActivityId", "TaskId", "DueDate", "CreatedTime", "Priority" },
            currentStep.Arguments);

        // Static columns carry data type + aggregable + system flags.
        var records = byName["Records"];
        var nameCol = records.StaticColumns.Single(c => c.Name == "Name");
        Assert.Equal("string", nameCol.DataType);
        Assert.True(nameCol.IsSystem);
        var dueCol = records.StaticColumns.Single(c => c.Name == "DueDate");
        Assert.Equal("date", dueCol.DataType);
        Assert.True(dueCol.IsAggregable);
    }

    [Fact]
    public async Task Flows_EntityContext_ExposesStatusValueCompletions()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/aql/schema/entity?name=Flows");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<EntityContextResponseDto>();
        Assert.NotNull(body);
        Assert.Equal("Flows", body!.Entity);
        Assert.Contains(body.Columns, c => c.Name == "Status");

        Assert.True(body.ValueCompletions.ContainsKey("Status"));
        var status = body.ValueCompletions["Status"];
        Assert.True(status.ClosedSet);
        Assert.Equal(
            new[] { "In-progress", "Completed", "Cancelled", "Suspended", "Terminated", "Errored" },
            status.Values);
    }

    [Fact]
    public async Task Notes_EntityContext_ExposesTypeValueCompletions()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/aql/schema/entity?name=Notes");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<EntityContextResponseDto>();
        Assert.NotNull(body);
        Assert.True(body!.ValueCompletions.ContainsKey("Type"));
        Assert.Equal(
            new[] { "Project", "Cabinet", "Notebook", "Page", "Note" },
            body.ValueCompletions["Type"].Values);
    }

    [Fact]
    public async Task Records_EntityContext_ReturnsStaticColumns_AndRecordTypeValueList()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/aql/schema/entity?name=Records");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<EntityContextResponseDto>();
        Assert.NotNull(body);

        // System columns are always present, regardless of RecordTypes in the DB.
        Assert.Contains(body!.Columns, c => c.Name == "Name");
        Assert.Contains(body.Columns, c => c.Name == "RecordType");
        Assert.Contains(body.Columns, c => c.Name == "CreatedDate");

        // RecordType value completions exist (the list may be empty when the
        // test DB has no seeded record types — the contract is that the key
        // is present and the values list reflects whatever is in the DB).
        Assert.True(body.ValueCompletions.ContainsKey("RecordType"));
    }

    [Fact]
    public async Task UnknownEntity_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/aql/schema/entity?name=NoSuchEntity");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("NoSuchEntity", doc.GetProperty("error").GetString());
    }
}
