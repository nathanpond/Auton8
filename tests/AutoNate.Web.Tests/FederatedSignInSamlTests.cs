using System.Net;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The SAML routes as HTTP: metadata, challenge, and the assertion consumer.
/// </summary>
/// <remarks>
/// <see cref="SamlSignInServiceTests"/> proves the assertion checks. These prove
/// the endpoints in front of them — in particular the one deliberate hole in the
/// CSRF model.
///
/// <c>POST /api/auth/saml/{slug}/acs</c> is the codebase's only endpoint that is
/// both anonymous and exempt from antiforgery, because the identity provider
/// posts to it cross-site and can carry neither an auth cookie nor a token for a
/// form this server never rendered. The exemption is sound only because the body
/// is itself a signed credential. So two things are pinned here: that the
/// exemption exists (or every SAML sign-in would 400), and that it buys an
/// attacker nothing (an arbitrary POST is still refused).
/// </remarks>
[Trait("Category", "Integration")]
public sealed class FederatedSignInSamlTests
{
    private const string Slug = "corp";

    private static async Task<(AutoNateWebApplicationFactory App, HttpClient Client)> BuildAsync(
        bool enabled = true)
    {
        var app = await AutoNateWebApplicationFactory.CreateAsync();
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            // The redirects are the assertion: following them would test the
            // login page instead of the endpoint.
            AllowAutoRedirect = false,
        });

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();

        await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Saml,
            DisplayName: "Corporate SAML",
            Slug: Slug,
            IsEnabled: enabled,
            OidcAuthority: null, OidcClientId: null, OidcScopes: null,
            SamlEntityId: "https://idp.example.com/saml",
            SamlMetadataUrl: "https://idp.example.com/saml/sso",
            SamlMetadataXml: null,
            SamlSigningCertificate: null,
            Secret: null), Guid.NewGuid(), CancellationToken.None);

        return (app, client);
    }

    [Fact]
    public async Task The_metadata_document_is_served_anonymously()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var response = await client.GetAsync($"/api/auth/saml/{Slug}/metadata");

        // Anonymous by necessity: an IdP administrator fetches this URL before
        // any account exists, and often from a machine that will never hold an
        // Auton8 session.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("EntityDescriptor", xml, StringComparison.Ordinal);
        Assert.Contains($"/api/auth/saml/{Slug}/acs", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_provider_publishes_no_metadata()
    {
        var (app, client) = await BuildAsync(enabled: false);
        await using var _ = app;

        var response = await client.GetAsync($"/api/auth/saml/{Slug}/metadata");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_provider_redirects_rather_than_enumerating()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var unknown = await client.GetAsync("/api/auth/saml/nope/challenge");
        var disabled = await client.GetAsync("/api/auth/saml/also-nope/challenge");

        // Both answers are identical on purpose. A signed-out visitor should not
        // be able to learn which providers exist but are switched off.
        Assert.Equal(HttpStatusCode.Redirect, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, disabled.StatusCode);
        Assert.Equal(unknown.Headers.Location, disabled.Headers.Location);
    }

    [Fact]
    public async Task The_assertion_consumer_is_exempt_from_antiforgery()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        // No antiforgery token, no cookie, no session — exactly what the IdP
        // sends. What must NOT come back is 400: that is the antiforgery
        // middleware refusing the request, and it would break every SAML
        // sign-in while looking like a protocol problem.
        var response = await client.PostAsync(
            $"/api/auth/saml/{Slug}/acs",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("SAMLResponse", "not-an-assertion")]));

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task An_arbitrary_post_to_the_assertion_consumer_signs_nobody_in()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var response = await client.PostAsync(
            $"/api/auth/saml/{Slug}/acs",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("SAMLResponse", "not-an-assertion")]));

        // The exemption above costs an attacker nothing, and this is the
        // assertion that says so: the request is refused, and — the part that
        // matters — no authentication cookie comes back with it.
        Assert.Equal("/?error=invalid", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.Contains(".AspNetCore.Cookies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_post_with_no_assertion_at_all_is_refused()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var response = await client.PostAsync(
            $"/api/auth/saml/{Slug}/acs", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?error=invalid", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_saml_provider_appears_on_the_public_provider_list()
    {
        var (app, client) = await BuildAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/api/auth/providers");

        // Display name, slug and kind — what a login button needs. The signing
        // certificate, metadata and entity ID stay behind the admin gate.
        Assert.Contains("\"kind\":\"saml\"", body, StringComparison.Ordinal);
        Assert.Contains("Corporate SAML", body, StringComparison.Ordinal);
        Assert.DoesNotContain("idp.example.com", body, StringComparison.Ordinal);
    }
}
