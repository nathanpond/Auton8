using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using Xunit;

namespace AutoNate.Web.Tests.Query;

[Trait("Category", "Integration")]
public sealed class SavedQueryEndpointTests
{
    [Fact]
    public async Task ListEmpty_OnFreshInstall()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/saved-queries/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<SavedQueryEndpoints.SavedQueryDto>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task Create_RoundTrips_AndShowsUpInList()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var createResp = await client.PostAsJsonAsync("/api/saved-queries/",
            new SavedQueryEndpoints.CreateSavedQueryRequest(
                Name: "Recent Cars",
                Description: "Cars created in the last two weeks.",
                QueryText: "FROM Records WHERE RecordType = \"Car\" AND CreatedDate > -2w",
                IsShared: false));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var saved = await createResp.Content.ReadFromJsonAsync<SavedQueryEndpoints.SavedQueryDto>();
        Assert.NotNull(saved);
        Assert.Equal("Recent Cars", saved!.Name);
        Assert.False(saved.IsShared);
        Assert.True(saved.IsOwn);

        var list = await client.GetFromJsonAsync<List<SavedQueryEndpoints.SavedQueryDto>>("/api/saved-queries/");
        Assert.Contains(list!, q => q.Id == saved.Id);
    }

    [Fact]
    public async Task Update_OwnRow_ChangesFields()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var created = await CreateAsync(client, "Edits");

        var patchResp = await client.PatchAsJsonAsync($"/api/saved-queries/{created.Id}",
            new SavedQueryEndpoints.UpdateSavedQueryRequest(
                Name: "Edits v2",
                Description: "renamed",
                QueryText: "FROM Records",
                IsShared: true));
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var updated = await patchResp.Content.ReadFromJsonAsync<SavedQueryEndpoints.SavedQueryDto>();
        Assert.Equal("Edits v2", updated!.Name);
        Assert.Equal("renamed", updated.Description);
        Assert.True(updated.IsShared);
        Assert.Equal("FROM Records", updated.QueryText);
    }

    [Fact]
    public async Task Create_RejectsDuplicateNameForSameOwner()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var first = await CreateAsync(client, "Dupe");

        var dupResp = await client.PostAsJsonAsync("/api/saved-queries/",
            new SavedQueryEndpoints.CreateSavedQueryRequest(
                Name: "Dupe",
                Description: null,
                QueryText: "FROM Records",
                IsShared: false));
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);
        Assert.NotEqual(Guid.Empty, first.Id);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var created = await CreateAsync(client, "Trash");

        var delResp = await client.DeleteAsync($"/api/saved-queries/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var list = await client.GetFromJsonAsync<List<SavedQueryEndpoints.SavedQueryDto>>("/api/saved-queries/");
        Assert.DoesNotContain(list!, q => q.Id == created.Id);
    }

    [Fact]
    public async Task Create_RejectsEmptyName()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync("/api/saved-queries/",
            new SavedQueryEndpoints.CreateSavedQueryRequest(
                Name: "  ",
                Description: null,
                QueryText: "FROM Records",
                IsShared: false));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static async Task<SavedQueryEndpoints.SavedQueryDto> CreateAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/saved-queries/",
            new SavedQueryEndpoints.CreateSavedQueryRequest(
                Name: name,
                Description: null,
                QueryText: "FROM Records",
                IsShared: false));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<SavedQueryEndpoints.SavedQueryDto>();
        Assert.NotNull(dto);
        return dto!;
    }
}
