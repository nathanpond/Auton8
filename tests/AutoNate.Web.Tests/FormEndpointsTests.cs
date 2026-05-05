using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class FormEndpointsTests
{
    [Fact]
    public async Task ListForms_OnEmptyDatabase_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var rows = await client.GetFromJsonAsync<List<FormSummaryDto>>("/api/forms");

        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    [Fact]
    public async Task CreateForm_RoundTrips()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateFormAsync(client, name: "Contact", shortCode: "Contact-Form");

        Assert.Equal("contact-form", created.ShortCode); // normalized to lowercase
        Assert.Equal("Contact", created.Name);
        Assert.True(created.IsDraft);
        Assert.Equal(1, created.DraftVersionNumber);
        Assert.Null(created.PublishedVersionNumber);

        var fetched = await client.GetFromJsonAsync<FormDto>($"/api/forms/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task SaveForm_BumpsVersionAndAppendsHistory()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateFormAsync(client, name: "Contact", shortCode: "contact");

        var updated = await SaveFormAsync(client, created.Id, new SaveFormRequestDto(
            Name: "Contact v2",
            ShortCode: "contact",
            FormCode: "function Page() { return <div>v2</div>; }",
            SiteAvailable: false));

        Assert.Equal(2, updated.DraftVersionNumber);
        Assert.True(updated.IsDraft);

        var versions = await client.GetFromJsonAsync<List<FormVersionDto>>(
            $"/api/forms/{created.Id}/versions");
        Assert.NotNull(versions);
        Assert.Equal(2, versions!.Count);
        Assert.Contains(versions, v => v.VersionNumber == 1 && v.Kind == "save");
        Assert.Contains(versions, v => v.VersionNumber == 2 && v.Kind == "save");
    }

    [Fact]
    public async Task PublishForm_SetsPublishedVersionAndClearsDraftFlag()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateFormAsync(client, name: "Contact", shortCode: "contact");

        var publishResp = await client.PostAsync($"/api/forms/{created.Id}/publish", content: null);
        publishResp.EnsureSuccessStatusCode();
        var published = await publishResp.Content.ReadFromJsonAsync<FormDto>();
        Assert.NotNull(published);
        Assert.False(published!.IsDraft);
        Assert.Equal(published.DraftVersionNumber, published.PublishedVersionNumber);

        // Publish writes its own version row.
        var versions = await client.GetFromJsonAsync<List<FormVersionDto>>(
            $"/api/forms/{created.Id}/versions");
        Assert.Contains(versions!, v => v.Kind == "publish");
    }

    [Fact]
    public async Task RestoreVersion_AppendsRestoreRow()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateFormAsync(client, name: "Contact", shortCode: "contact");
        await SaveFormAsync(client, created.Id, new SaveFormRequestDto(
            Name: created.Name,
            ShortCode: created.ShortCode,
            FormCode: "function Page() { return <div>v2</div>; }",
            SiteAvailable: false));

        // Restore v1 — should write a v3 row of kind 'restore'.
        var restoreResp = await client.PostAsync($"/api/forms/{created.Id}/restore/1", content: null);
        restoreResp.EnsureSuccessStatusCode();
        var restored = await restoreResp.Content.ReadFromJsonAsync<FormDto>();
        Assert.NotNull(restored);
        Assert.Equal(3, restored!.DraftVersionNumber);

        var versions = await client.GetFromJsonAsync<List<FormVersionDto>>(
            $"/api/forms/{created.Id}/versions");
        Assert.Contains(versions!, v => v.VersionNumber == 3 && v.Kind == "restore");
    }

    [Fact]
    public async Task PublicEndpoint_404sWhenUnpublishedOrHidden()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateFormAsync(client, name: "Contact", shortCode: "contact");

        // Not published yet
        var resp1 = await client.GetAsync($"/api/forms/public/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.NotFound, resp1.StatusCode);

        // Publish but site_available=false
        var publishResp = await client.PostAsync($"/api/forms/{created.Id}/publish", content: null);
        publishResp.EnsureSuccessStatusCode();

        var resp2 = await client.GetAsync($"/api/forms/public/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.NotFound, resp2.StatusCode);

        // Flip site_available + re-save (no need to re-publish — published_version
        // points to the same row, but site_available flips on the live form).
        await SaveFormAsync(client, created.Id, new SaveFormRequestDto(
            Name: created.Name,
            ShortCode: created.ShortCode,
            FormCode: "function Page() { return <div>live</div>; }",
            SiteAvailable: true));
        // Still requires another publish so the version snapshot is fresh.
        (await client.PostAsync($"/api/forms/{created.Id}/publish", content: null))
            .EnsureSuccessStatusCode();

        var resp3 = await client.GetAsync($"/api/forms/public/{created.ShortCode}");
        resp3.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeleteForm_CascadesVersions()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateFormAsync(client, name: "Temp", shortCode: "temp");

        var deleteResp = await client.DeleteAsync($"/api/forms/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getResp = await client.GetAsync($"/api/forms/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);

        var versionsResp = await client.GetAsync($"/api/forms/{created.Id}/versions");
        // Versions endpoint requires Form.View on the form id; missing form
        // means the instance authorizer returns false → 403 with auth on,
        // but auth is off here, so we get an empty list.
        versionsResp.EnsureSuccessStatusCode();
        var versions = await versionsResp.Content.ReadFromJsonAsync<List<FormVersionDto>>();
        Assert.Empty(versions!);
    }

    [Fact]
    public async Task ListForms_Returns403_WithoutPermission()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
        });
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/forms");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        // Dev auto-login fires on GETs; trigger the cookie before any POST.
        (await client.GetAsync("/api/forms")).EnsureSuccessStatusCode();
    }

    private static async Task<FormDto> CreateFormAsync(HttpClient client, string name, string shortCode)
    {
        var resp = await client.PostAsJsonAsync("/api/forms", new CreateFormRequestDto(
            Name: name,
            ShortCode: shortCode,
            FormCode: null,
            SiteAvailable: false));
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<FormDto>();
        Assert.NotNull(created);
        return created!;
    }

    private static async Task<FormDto> SaveFormAsync(HttpClient client, Guid id, SaveFormRequestDto request)
    {
        var resp = await client.PutAsJsonAsync($"/api/forms/{id}", request);
        resp.EnsureSuccessStatusCode();
        var saved = await resp.Content.ReadFromJsonAsync<FormDto>();
        Assert.NotNull(saved);
        return saved!;
    }

    private sealed record CreateFormRequestDto(
        string Name,
        string ShortCode,
        string? FormCode,
        bool? SiteAvailable);

    private sealed record SaveFormRequestDto(
        string Name,
        string ShortCode,
        string FormCode,
        bool SiteAvailable);

    private sealed record FormDto(
        Guid Id,
        string Name,
        string ShortCode,
        string FormCode,
        bool SiteAvailable,
        bool IsDraft,
        int DraftVersionNumber,
        int? PublishedVersionNumber,
        DateTimeOffset CreatedAtUtc,
        Guid CreatedBy,
        DateTimeOffset UpdatedAtUtc,
        Guid UpdatedBy);

    private sealed record FormSummaryDto(
        Guid Id,
        string Name,
        string ShortCode,
        bool SiteAvailable,
        bool IsDraft,
        int DraftVersionNumber,
        int? PublishedVersionNumber,
        DateTimeOffset UpdatedAtUtc);

    private sealed record FormVersionDto(
        Guid Id,
        Guid FormId,
        int VersionNumber,
        string Name,
        string ShortCode,
        string FormCode,
        bool SiteAvailable,
        string Kind,
        string? Note,
        DateTimeOffset CreatedAtUtc,
        Guid CreatedBy);
}
