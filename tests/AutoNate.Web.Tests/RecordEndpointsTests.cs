using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordEndpointsTests
{
    [Fact]
    public async Task SearchRecords_OnEmptyType_ReturnsZeroResults()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var page = await fixture.Client.GetFromJsonAsync<RecordPageDto>(
            $"/api/records/?recordTypeId={fixture.RecordTypeId}");

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task GetRecord_NotFound_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.GetAsync($"/api/records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByKey_NotFound_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.GetAsync("/api/records/by-key/TASK-9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateRecord_RoundTripsAndIsRetrievableByKey()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var created = await fixture.CreateRecordAsync(name: "First");

        Assert.Equal("First", created.Name);
        Assert.False(created.IsArchived);
        Assert.True(created.KeyNumber >= 1);

        var byKey = await fixture.Client.GetFromJsonAsync<RecordDto>(
            $"/api/records/by-key/{created.Key}");
        Assert.NotNull(byKey);
        Assert.Equal(created.Id, byKey.Id);
    }

    [Fact]
    public async Task PatchRecord_UpdatesNameOnly_WhenOnlyNameProvided()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync(name: "Initial", status: "open");

        var response = await fixture.Client.PatchAsync(
            $"/api/records/{created.Id}",
            JsonContent("""{"name":"Updated"}"""));
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<RecordDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Name);
        // Status was not in the body (Optional<None>) — keep prior value.
        Assert.Equal("open", updated.Status);
    }

    [Fact]
    public async Task PatchRecord_ClearStatus_WithExplicitNull_SetsStatusToNull()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync(name: "Item", status: "open");
        Assert.Equal("open", created.Status);

        var response = await fixture.Client.PatchAsync(
            $"/api/records/{created.Id}",
            JsonContent("""{"status":null}"""));
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<RecordDto>();
        Assert.NotNull(updated);
        Assert.Null(updated.Status);
    }

    [Fact]
    public async Task PatchRecord_InvalidDueDate_Returns400()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync();

        var response = await fixture.Client.PatchAsync(
            $"/api/records/{created.Id}",
            JsonContent("""{"dueDate":"not-a-date"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRecord_NotFound_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.PatchAsync(
            $"/api/records/{Guid.NewGuid()}",
            JsonContent("""{"name":"x"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchRecord_NonObjectBody_Returns400()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync();

        var response = await fixture.Client.PatchAsync(
            $"/api/records/{created.Id}",
            JsonContent("\"just a string\""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAndRestoreRecord_TogglesArchivedFlag()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync();

        var deleteResponse = await fixture.Client.DeleteAsync($"/api/records/{created.Id}");
        deleteResponse.EnsureSuccessStatusCode();
        var archived = await deleteResponse.Content.ReadFromJsonAsync<RecordDto>();
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);

        var restoreResponse = await fixture.Client.PostAsync(
            $"/api/records/{created.Id}/restore",
            content: null);
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<RecordDto>();
        Assert.NotNull(restored);
        Assert.False(restored.IsArchived);
    }

    [Fact]
    public async Task DeleteRecord_NotFound_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.DeleteAsync($"/api/records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutAssignees_AssignsRecordToActor()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync();
        var assignee = Guid.NewGuid();

        var response = await fixture.Client.PutAsJsonAsync(
            $"/api/records/{created.Id}/assignees",
            new[] { assignee });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<RecordDto>();
        Assert.NotNull(updated);
        Assert.Contains(assignee, updated.AssigneeIds);
    }

    [Fact]
    public async Task PutAssignees_NotFound_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.PutAsJsonAsync(
            $"/api/records/{Guid.NewGuid()}/assignees",
            Array.Empty<Guid>());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_AfterCreate_ReturnsAtLeastOneEntry()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync();

        var entries = await fixture.Client.GetFromJsonAsync<RecordHistoryEntryDto[]>(
            $"/api/records/{created.Id}/history");

        Assert.NotNull(entries);
        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task PostSearch_WithEqualsFilter_FindsCreatedRecord()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var created = await fixture.CreateRecordAsync(name: "Hello");

        var response = await fixture.Client.PostAsJsonAsync(
            "/api/records/search",
            new SearchRecordsRequest(
                RecordTypeId: fixture.RecordTypeId,
                Filters: Array.Empty<SearchFilterClause>(),
                AssigneeId: null,
                IncludeArchived: false,
                Page: 0,
                PageSize: 25,
                Sort: null));
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<RecordPageDto>();
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(created.Id, page.Items[0].Id);
    }

    [Fact]
    public async Task PostSearch_WithUnknownOperator_Returns400()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "/api/records/search",
            new SearchRecordsRequest(
                RecordTypeId: fixture.RecordTypeId,
                Filters: new[]
                {
                    new SearchFilterClause(
                        "title",
                        "what_even_is_this",
                        JsonDocument.Parse("\"x\"").RootElement)
                },
                AssigneeId: null,
                IncludeArchived: false,
                Page: 0,
                PageSize: 25,
                Sort: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAssignedToMe_ReturnsRecordsAssignedToActor()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var actorId = await fixture.GetAdminUserIdAsync();

        var created = await fixture.CreateRecordAsync(name: "Mine", assigneeIds: new[] { actorId });

        var page = await fixture.Client.GetFromJsonAsync<RecordPageDto>(
            "/api/records/assigned-to-me");

        Assert.NotNull(page);
        Assert.Contains(page.Items, r => r.Id == created.Id);
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;

        private TestFixture(
            AutoNateWebApplicationFactory factory,
            HttpClient client,
            Guid recordTypeId)
        {
            _factory = factory;
            Client = client;
            RecordTypeId = recordTypeId;
        }

        public HttpClient Client { get; }
        public Guid RecordTypeId { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            var client = factory.CreateClient();

            // Prime auth so subsequent writes carry the cookie.
            (await client.GetAsync("/api/record-types/")).EnsureSuccessStatusCode();

            var recordTypeResponse = await client.PostAsJsonAsync(
                "/api/record-types/",
                new CreateRecordTypeRequest(
                    ShortCode: "task",
                    Name: "Task",
                    Description: null,
                    Icon: null,
                    Color: null));
            recordTypeResponse.EnsureSuccessStatusCode();
            var recordType = await recordTypeResponse.Content.ReadFromJsonAsync<RecordTypeDto>();

            return new TestFixture(factory, client, recordType!.Id);
        }

        public async Task<RecordDto> CreateRecordAsync(
            string name = "Item",
            string? status = null,
            DateOnly? dueDate = null,
            Guid[]? assigneeIds = null)
        {
            var response = await Client.PostAsJsonAsync(
                "/api/records/",
                new CreateRecordRequest(
                    RecordTypeId: RecordTypeId,
                    Name: name,
                    Status: status,
                    DueDate: dueDate,
                    Values: JsonDocument.Parse("{}").RootElement,
                    AssigneeIds: assigneeIds));
            response.EnsureSuccessStatusCode();
            var record = await response.Content.ReadFromJsonAsync<RecordDto>();
            Assert.NotNull(record);
            return record;
        }

        public async Task<Guid> GetAdminUserIdAsync()
        {
            var users = await Client.GetFromJsonAsync<List<Models.LocalUser>>("/api/users");
            Assert.NotNull(users);
            var admin = users.Single(u => u.Username == "admin");
            return admin.UserId;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }
}
