using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The claim → group mapping admin surface (#92).
/// </summary>
/// <remarks>
/// The preview is the part worth testing hardest. It is the only way to check a
/// mapping without asking a user to sign in repeatedly, so an administrator will
/// believe it — which makes a preview that can be wrong worse than no preview at
/// all. <see cref="ClaimGroupReconcilerTests"/> pins that it agrees with an
/// actual sign-in; these pin that the endpoint in front of it says the same
/// thing.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class IdentityProviderGroupMappingEndpointsTests
{
    private static async Task<(AutoNateWebApplicationFactory App, HttpClient Client, Guid Provider)>
        BuildAsync()
    {
        var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient();

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        var provider = await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Oidc,
            DisplayName: "Corporate",
            Slug: "corp",
            IsEnabled: true,
            OidcAuthority: "https://idp.example.com",
            OidcClientId: "auton8",
            OidcScopes: null,
            SamlEntityId: null, SamlMetadataUrl: null, SamlMetadataXml: null,
            SamlSigningCertificate: null,
            Secret: null), Guid.NewGuid(), CancellationToken.None);

        // The factory's Development auto-login middleware only activates on
        // GET, so a POST from a fresh client has no actor and the handler
        // refuses it. One GET puts the cookie in the client's container.
        (await client.GetAsync("/api/admin/identity-providers")).EnsureSuccessStatusCode();

        return (app, client, provider.Id);
    }

    private static async Task<Guid> GroupAsync(AutoNateWebApplicationFactory app, string name)
    {
        var groups = app.Services.CreateScope().ServiceProvider.GetRequiredService<IGroupStore>();
        var group = await groups.CreateAsync(new CreateGroupInput(name, null), Guid.NewGuid());
        return group.Id;
    }

    [Fact]
    public async Task A_mapping_can_be_created_listed_edited_and_deleted()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var engineering = await GroupAsync(app, "Engineering");
        var sales = await GroupAsync(app, "Sales");
        var baseUrl = $"/api/admin/identity-providers/{provider}/group-mappings";

        var created = await client.PostAsJsonAsync(
            baseUrl, new UpsertGroupMappingRequest("groups", "engineering", engineering));
        created.EnsureSuccessStatusCode();
        var mapping = await created.Content.ReadFromJsonAsync<IdentityProviderGroupMappingDto>();
        Assert.NotNull(mapping);
        Assert.Equal("Engineering", mapping!.GroupName);

        var listed = await client.GetFromJsonAsync<List<IdentityProviderGroupMappingDto>>(baseUrl);
        Assert.Single(listed!);

        var edited = await client.PutAsJsonAsync(
            $"{baseUrl}/{mapping.Id}", new UpsertGroupMappingRequest("groups", "sales", sales));
        edited.EnsureSuccessStatusCode();
        var updated = await edited.Content.ReadFromJsonAsync<IdentityProviderGroupMappingDto>();
        Assert.Equal("Sales", updated!.GroupName);
        Assert.Equal("sales", updated.ClaimValue);

        var deleted = await client.DeleteAsync($"{baseUrl}/{mapping.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<List<IdentityProviderGroupMappingDto>>(baseUrl) ?? []);
    }

    [Fact]
    public async Task The_same_edge_cannot_be_created_twice()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var group = await GroupAsync(app, "Engineering");
        var baseUrl = $"/api/admin/identity-providers/{provider}/group-mappings";
        var body = new UpsertGroupMappingRequest("groups", "engineering", group);

        (await client.PostAsJsonAsync(baseUrl, body)).EnsureSuccessStatusCode();
        var second = await client.PostAsJsonAsync(baseUrl, body);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Several_claims_may_grant_one_group_and_one_claim_several_groups()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var engineering = await GroupAsync(app, "Engineering");
        var oncall = await GroupAsync(app, "On Call");
        var baseUrl = $"/api/admin/identity-providers/{provider}/group-mappings";

        // Neither of these is the duplicate the unique index forbids. Only the
        // same edge twice is meaningless; a fan-out in either direction is an
        // ordinary way to express access.
        (await client.PostAsJsonAsync(baseUrl,
            new UpsertGroupMappingRequest("groups", "engineering", engineering))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(baseUrl,
            new UpsertGroupMappingRequest("groups", "engineering", oncall))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(baseUrl,
            new UpsertGroupMappingRequest("groups", "sre", oncall))).EnsureSuccessStatusCode();

        Assert.Equal(3, (await client.GetFromJsonAsync<List<IdentityProviderGroupMappingDto>>(baseUrl))!.Count);
    }

    [Fact]
    public async Task A_mapping_with_no_claim_value_is_refused()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var group = await GroupAsync(app, "Engineering");
        var baseUrl = $"/api/admin/identity-providers/{provider}/group-mappings";

        // A claim type with no value would grant the group to everyone carrying
        // that claim at all, whatever its contents — which is the accidental
        // "everyone in the company" rule.
        var response = await client.PostAsJsonAsync(
            baseUrl, new UpsertGroupMappingRequest("groups", "   ", group));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("claim value is required", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_mapping_pointing_at_no_group_is_refused()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var response = await client.PostAsJsonAsync(
            $"/api/admin/identity-providers/{provider}/group-mappings",
            new UpsertGroupMappingRequest("groups", "engineering", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mappings_are_scoped_to_their_provider()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        var other = await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Saml, DisplayName: "Partner", Slug: "partner",
            IsEnabled: true, OidcAuthority: null, OidcClientId: null, OidcScopes: null,
            SamlEntityId: "https://partner.example.com", SamlMetadataUrl: null,
            SamlMetadataXml: null, SamlSigningCertificate: null, Secret: null),
            Guid.NewGuid(), CancellationToken.None);

        var group = await GroupAsync(app, "Engineering");
        var created = await client.PostAsJsonAsync(
            $"/api/admin/identity-providers/{provider}/group-mappings",
            new UpsertGroupMappingRequest("groups", "engineering", group));
        var mapping = await created.Content.ReadFromJsonAsync<IdentityProviderGroupMappingDto>();

        // Reaching another provider's mapping through your own provider's path
        // would let one provider's administrator edit another's access rules.
        var stolen = await client.DeleteAsync(
            $"/api/admin/identity-providers/{other.Id}/group-mappings/{mapping!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);

        Assert.Empty(await client.GetFromJsonAsync<List<IdentityProviderGroupMappingDto>>(
            $"/api/admin/identity-providers/{other.Id}/group-mappings") ?? []);
    }

    [Fact]
    public async Task The_preview_reports_what_a_claim_set_would_grant()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var engineering = await GroupAsync(app, "Engineering");
        var oncall = await GroupAsync(app, "On Call");
        await GroupAsync(app, "Sales");
        var baseUrl = $"/api/admin/identity-providers/{provider}/group-mappings";

        (await client.PostAsJsonAsync(baseUrl,
            new UpsertGroupMappingRequest("groups", "engineering", engineering))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(baseUrl,
            new UpsertGroupMappingRequest("groups", "engineering", oncall))).EnsureSuccessStatusCode();

        var preview = await client.PostAsJsonAsync($"{baseUrl}/preview", new
        {
            claims = new Dictionary<string, string[]>
            {
                ["groups"] = ["engineering", "some-group-nobody-mapped"],
            },
        });
        preview.EnsureSuccessStatusCode();

        var granted = await preview.Content.ReadFromJsonAsync<JsonElement>();
        var names = granted.EnumerateArray()
            .Select(g => g.GetProperty("name").GetString())
            .Order()
            .ToList();

        Assert.Equal(["Engineering", "On Call"], names);
    }

    [Fact]
    public async Task The_preview_of_an_unmapped_claim_set_is_empty_rather_than_an_error()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var preview = await client.PostAsJsonAsync(
            $"/api/admin/identity-providers/{provider}/group-mappings/preview",
            new { claims = new Dictionary<string, string[]> { ["groups"] = ["nothing-maps-this"] } });

        preview.EnsureSuccessStatusCode();
        var granted = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, granted.GetArrayLength());
    }

    [Fact]
    public async Task Deleting_a_provider_takes_its_mappings_with_it()
    {
        var (app, client, provider) = await BuildAsync();
        await using var _ = app;

        var group = await GroupAsync(app, "Engineering");
        (await client.PostAsJsonAsync(
            $"/api/admin/identity-providers/{provider}/group-mappings",
            new UpsertGroupMappingRequest("groups", "engineering", group))).EnsureSuccessStatusCode();

        // A mapping outliving its provider would point at nothing, and
        // reconciliation would have to decide what a dangling grant means — a
        // question better not to have. The schema cascades so it never arises.
        (await client.DeleteAsync($"/api/admin/identity-providers/{provider}")).EnsureSuccessStatusCode();

        var db = app.Services.GetRequiredService<
            Microsoft.EntityFrameworkCore.IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        await using var ctx = await db.CreateDbContextAsync();
        Assert.Empty(ctx.IdentityProviderGroupMappings.ToList());
    }
}
