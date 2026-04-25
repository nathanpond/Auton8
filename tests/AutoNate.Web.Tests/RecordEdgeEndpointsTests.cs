using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordEdgeEndpointsTests
{
    [Fact]
    public async Task ListEdgeTypes_OnEmptyDatabase_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var types = await client.GetFromJsonAsync<EdgeTypeDto[]>("/api/record-edge-types/");

        Assert.NotNull(types);
        Assert.Empty(types);
    }

    [Fact]
    public async Task GetEdgeType_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/record-edge-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EdgeTypeCrud_FullRoundTrip()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var edgeType = await fixture.CreateEdgeTypeAsync("rel");
        Assert.False(edgeType.IsArchived);

        // Update name
        var updateResponse = await fixture.Client.PatchAsJsonAsync(
            $"/api/record-edge-types/{edgeType.Id}",
            new UpdateEdgeTypeRequest(
                Name: "Renamed",
                InverseName: null,
                IsDirected: edgeType.IsDirected,
                AllowSelfReference: edgeType.AllowSelfReference,
                Cardinality: edgeType.Cardinality,
                FromRecordTypeIds: null,
                ToRecordTypeIds: null));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<EdgeTypeDto>();
        Assert.Equal("Renamed", updated!.Name);

        // Archive then restore
        var deleteResponse = await fixture.Client.DeleteAsync($"/api/record-edge-types/{edgeType.Id}");
        deleteResponse.EnsureSuccessStatusCode();
        var archived = await deleteResponse.Content.ReadFromJsonAsync<EdgeTypeDto>();
        Assert.True(archived!.IsArchived);

        var restoreResponse = await fixture.Client.PostAsync(
            $"/api/record-edge-types/{edgeType.Id}/restore",
            content: null);
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<EdgeTypeDto>();
        Assert.False(restored!.IsArchived);
    }

    [Fact]
    public async Task EdgeTypeFieldsCrud_RoundTrips()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var edgeType = await fixture.CreateEdgeTypeAsync("rel");

        var emptyConfig = JsonDocument.Parse("{}").RootElement;
        var createResponse = await fixture.Client.PostAsJsonAsync(
            $"/api/record-edge-types/{edgeType.Id}/fields",
            new CreateEdgeFieldRequest(
                FieldKey: "weight",
                DisplayName: "Weight",
                DataType: "number",
                Config: emptyConfig,
                IsRequired: false,
                SortOrder: 0));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var field = await createResponse.Content.ReadFromJsonAsync<EdgeTypeFieldDto>();

        var fields = await fixture.Client.GetFromJsonAsync<EdgeTypeFieldDto[]>(
            $"/api/record-edge-types/{edgeType.Id}/fields");
        Assert.NotNull(fields);
        Assert.Single(fields);

        var deleteResponse = await fixture.Client.DeleteAsync(
            $"/api/record-edge-types/{edgeType.Id}/fields/{field!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateEdge_WithUnknownEdgeType_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "/api/record-edges/",
            new CreateEdgeRequest(
                EdgeTypeId: Guid.NewGuid(),
                FromRecordId: fixture.RecordA,
                ToRecordId: fixture.RecordB,
                Data: JsonDocument.Parse("{}").RootElement));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateEdge_RoundTripsAndShowsInListForRecord()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var edgeType = await fixture.CreateEdgeTypeAsync("rel");

        var createResponse = await fixture.Client.PostAsJsonAsync(
            "/api/record-edges/",
            new CreateEdgeRequest(
                EdgeTypeId: edgeType.Id,
                FromRecordId: fixture.RecordA,
                ToRecordId: fixture.RecordB,
                Data: JsonDocument.Parse("{}").RootElement));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var edge = await createResponse.Content.ReadFromJsonAsync<EdgeDto>();
        Assert.NotNull(edge);

        var edgesForA = await fixture.Client.GetFromJsonAsync<EdgeDto[]>(
            $"/api/records/{fixture.RecordA}/edges");
        Assert.NotNull(edgesForA);
        Assert.Contains(edgesForA, e => e.Id == edge.Id);

        var deleteResponse = await fixture.Client.DeleteAsync($"/api/record-edges/{edge.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task TraverseEdges_FromRecordA_FindsRecordB()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var edgeType = await fixture.CreateEdgeTypeAsync("rel");

        (await fixture.Client.PostAsJsonAsync(
            "/api/record-edges/",
            new CreateEdgeRequest(
                EdgeTypeId: edgeType.Id,
                FromRecordId: fixture.RecordA,
                ToRecordId: fixture.RecordB,
                Data: JsonDocument.Parse("{}").RootElement))).EnsureSuccessStatusCode();

        var response = await fixture.Client.PostAsJsonAsync(
            $"/api/records/{fixture.RecordA}/traverse",
            new TraverseHttpRequest(
                StartRecordIds: Array.Empty<Guid>(),
                EdgeTypeIds: null,
                Direction: "outgoing",
                MaxHops: 1));
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<TraverseResultDto[]>();
        Assert.NotNull(rows);
        Assert.Contains(rows, r => r.RecordId == fixture.RecordB);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;

        private TestFixture(
            AutoNateWebApplicationFactory factory,
            HttpClient client,
            Guid recordA,
            Guid recordB)
        {
            _factory = factory;
            Client = client;
            RecordA = recordA;
            RecordB = recordB;
        }

        public HttpClient Client { get; }
        public Guid RecordA { get; }
        public Guid RecordB { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            var client = factory.CreateClient();
            (await client.GetAsync("/api/record-types/")).EnsureSuccessStatusCode();

            var recordTypeResponse = await client.PostAsJsonAsync(
                "/api/record-types/",
                new CreateRecordTypeRequest("task", "Task", null, null, null));
            recordTypeResponse.EnsureSuccessStatusCode();
            var recordType = await recordTypeResponse.Content.ReadFromJsonAsync<RecordTypeDto>();

            var recordA = await CreateRecordAsync(client, recordType!.Id, "A");
            var recordB = await CreateRecordAsync(client, recordType.Id, "B");

            return new TestFixture(factory, client, recordA, recordB);
        }

        public async Task<EdgeTypeDto> CreateEdgeTypeAsync(string shortCode)
        {
            var response = await Client.PostAsJsonAsync(
                "/api/record-edge-types/",
                new CreateEdgeTypeRequest(
                    ShortCode: shortCode,
                    Name: "Relates To",
                    InverseName: null,
                    IsDirected: true,
                    AllowSelfReference: false,
                    Cardinality: "many_to_many",
                    FromRecordTypeIds: null,
                    ToRecordTypeIds: null));
            response.EnsureSuccessStatusCode();
            var edgeType = await response.Content.ReadFromJsonAsync<EdgeTypeDto>();
            Assert.NotNull(edgeType);
            return edgeType;
        }

        private static async Task<Guid> CreateRecordAsync(HttpClient client, Guid recordTypeId, string name)
        {
            var response = await client.PostAsJsonAsync(
                "/api/records/",
                new CreateRecordRequest(
                    RecordTypeId: recordTypeId,
                    Name: name,
                    Status: null,
                    DueDate: null,
                    Values: JsonDocument.Parse("{}").RootElement,
                    AssigneeIds: null));
            response.EnsureSuccessStatusCode();
            var record = await response.Content.ReadFromJsonAsync<RecordDto>();
            return record!.Id;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }
}
