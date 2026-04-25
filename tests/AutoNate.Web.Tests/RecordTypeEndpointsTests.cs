using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordTypeEndpointsTests
{
    [Fact]
    public async Task GetFieldTypes_ReturnsSevenRegisteredTypes()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var types = await client.GetFromJsonAsync<List<FieldTypeMetadataDto>>(
            "/api/record-types/field-types");

        Assert.NotNull(types);
        var names = types.Select(t => t.DataType).OrderBy(n => n).ToList();
        Assert.Equal(
            new[] { "boolean", "date", "email", "number", "option", "phone", "text" },
            names);
    }

    [Fact]
    public async Task ListRecordTypes_OnEmptyDatabase_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var types = await client.GetFromJsonAsync<List<RecordTypeDto>>("/api/record-types/");

        Assert.NotNull(types);
        Assert.Empty(types);
    }

    [Fact]
    public async Task CreateRecordType_RoundTrips()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateRecordTypeAsync(client, shortCode: "task", name: "Task");

        // Store normalizes short_code to upper case.
        Assert.Equal("TASK", created.ShortCode);
        Assert.Equal("Task", created.Name);
        Assert.False(created.IsArchived);

        var fetched = await client.GetFromJsonAsync<RecordTypeDto>(
            $"/api/record-types/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
    }

    [Fact]
    public async Task CreateRecordType_InvalidShortCode_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/record-types/",
            new CreateRecordTypeRequest(
                ShortCode: "1bad",
                Name: "Bad",
                Description: null,
                Icon: null,
                Color: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRecordType_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/record-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRecordType_ChangesName()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateRecordTypeAsync(client, shortCode: "task", name: "Task");

        var response = await client.PatchAsJsonAsync(
            $"/api/record-types/{created.Id}",
            new UpdateRecordTypeRequest(
                Name: "Updated",
                Description: "desc",
                Icon: null,
                Color: null));

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<RecordTypeDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal("desc", updated.Description);
    }

    [Fact]
    public async Task DeleteAndRestoreRecordType_TogglesArchivedFlag()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateRecordTypeAsync(client, shortCode: "task", name: "Task");

        var deleteResponse = await client.DeleteAsync($"/api/record-types/{created.Id}");
        deleteResponse.EnsureSuccessStatusCode();
        var archived = await deleteResponse.Content.ReadFromJsonAsync<RecordTypeDto>();
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);

        var restoreResponse = await client.PostAsync(
            $"/api/record-types/{created.Id}/restore",
            content: null);
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<RecordTypeDto>();
        Assert.NotNull(restored);
        Assert.False(restored.IsArchived);
    }

    [Fact]
    public async Task DeleteRecordType_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.DeleteAsync($"/api/record-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FieldsCrud_AllOperationsRoundTrip()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var recordType = await CreateRecordTypeAsync(client, shortCode: "task", name: "Task");
        var emptyConfig = JsonDocument.Parse("{}").RootElement;

        // Create field
        var createResponse = await client.PostAsJsonAsync(
            $"/api/record-types/{recordType.Id}/fields",
            new CreateFieldRequest(
                FieldKey: "title",
                DisplayName: "Title",
                DataType: "text",
                Config: emptyConfig,
                IsRequired: true,
                SortOrder: 0));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var field = await createResponse.Content.ReadFromJsonAsync<RecordTypeFieldDto>();
        Assert.NotNull(field);

        // Get field
        var fetched = await client.GetFromJsonAsync<RecordTypeFieldDto>(
            $"/api/record-types/{recordType.Id}/fields/{field.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("title", fetched.FieldKey);
        Assert.True(fetched.IsRequired);

        // List fields
        var fields = await client.GetFromJsonAsync<List<RecordTypeFieldDto>>(
            $"/api/record-types/{recordType.Id}/fields");
        Assert.NotNull(fields);
        Assert.Single(fields);

        // Update field
        var updateResponse = await client.PatchAsJsonAsync(
            $"/api/record-types/{recordType.Id}/fields/{field.Id}",
            new UpdateFieldRequest(
                DisplayName: "New Title",
                Config: emptyConfig,
                IsRequired: false,
                SortOrder: 1));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<RecordTypeFieldDto>();
        Assert.NotNull(updated);
        Assert.Equal("New Title", updated.DisplayName);
        Assert.False(updated.IsRequired);

        // Archive field
        var deleteResponse = await client.DeleteAsync(
            $"/api/record-types/{recordType.Id}/fields/{field.Id}");
        deleteResponse.EnsureSuccessStatusCode();
        var archived = await deleteResponse.Content.ReadFromJsonAsync<RecordTypeFieldDto>();
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);

        // Restore field
        var restoreResponse = await client.PostAsync(
            $"/api/record-types/{recordType.Id}/fields/{field.Id}/restore",
            content: null);
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<RecordTypeFieldDto>();
        Assert.NotNull(restored);
        Assert.False(restored.IsArchived);
    }

    [Fact]
    public async Task CreateField_UnknownDataType_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var recordType = await CreateRecordTypeAsync(client, shortCode: "task", name: "Task");
        var emptyConfig = JsonDocument.Parse("{}").RootElement;

        var response = await client.PostAsJsonAsync(
            $"/api/record-types/{recordType.Id}/fields",
            new CreateFieldRequest(
                FieldKey: "title",
                DisplayName: "Title",
                DataType: "not_a_real_type",
                Config: emptyConfig,
                IsRequired: false,
                SortOrder: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateField_RecordTypeNotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var emptyConfig = JsonDocument.Parse("{}").RootElement;
        var response = await client.PostAsJsonAsync(
            $"/api/record-types/{Guid.NewGuid()}/fields",
            new CreateFieldRequest(
                FieldKey: "title",
                DisplayName: "Title",
                DataType: "text",
                Config: emptyConfig,
                IsRequired: false,
                SortOrder: 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListAudit_AfterCreate_ReturnsAtLeastOneEntry()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateRecordTypeAsync(client, shortCode: "task", name: "Task");

        var audit = await client.GetFromJsonAsync<List<RecordTypeAuditDto>>(
            $"/api/record-types/{created.Id}/audit");

        Assert.NotNull(audit);
        Assert.NotEmpty(audit);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        // Dev auto-login only fires on non-POST requests; trigger it with a GET
        // so the cookie is captured for subsequent writes.
        (await client.GetAsync("/api/record-types/")).EnsureSuccessStatusCode();
    }

    private static async Task<RecordTypeDto> CreateRecordTypeAsync(
        HttpClient client,
        string shortCode,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/record-types/",
            new CreateRecordTypeRequest(
                ShortCode: shortCode,
                Name: name,
                Description: null,
                Icon: null,
                Color: null));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<RecordTypeDto>();
        Assert.NotNull(created);
        return created;
    }
}
