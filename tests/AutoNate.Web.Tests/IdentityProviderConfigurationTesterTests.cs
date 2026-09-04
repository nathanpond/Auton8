using System.Net;
using System.Text;
using AutoNate.Web.Models;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Behaviour of the "test configuration" action (#87).
/// </summary>
/// <remarks>
/// The point of this action is that a typo in an issuer URL is reported when
/// the provider is saved rather than at someone's first sign-in attempt — at
/// which point the person hitting it is a user who cannot get in and has no way
/// to see why. So the cases that matter are the failing ones, and every one of
/// them must produce a message rather than an exception.
/// </remarks>
public sealed class IdentityProviderConfigurationTesterTests
{
    private const string Host = "idp.example.com";

    private static IdentityProviderConfigurationTester TesterFor(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var options = Options.Create(new ExternalConnectionUrlOptions
        {
            AllowedProviderHosts = new(StringComparer.OrdinalIgnoreCase)
            {
                [IdentityProviderConfigurationTester.OidcPolicyKind] = [Host],
                [IdentityProviderConfigurationTester.SamlPolicyKind] = [Host],
            },
        });

        return new IdentityProviderConfigurationTester(
            new StubHttpClientFactory(respond),
            new ProviderBaseUrlPolicy(options),
            NullLogger<IdentityProviderConfigurationTester>.Instance);
    }

    private static IdentityProviderDto Oidc(string? authority = null) => new(
        Guid.NewGuid(), IdentityProviderKinds.Oidc, "Corporate", "corporate", false,
        authority ?? $"https://{Host}/realms/auton8", "client-id", null,
        null, null, false, null, false, null, DateTime.UtcNow, DateTime.UtcNow);

    private static IdentityProviderDto Saml(string? metadataUrl = null, bool inlineXml = false) => new(
        Guid.NewGuid(), IdentityProviderKinds.Saml, "Corporate SAML", "corporate-saml", false,
        null, null, null,
        $"https://{Host}/entity", metadataUrl, inlineXml, null, false, null,
        DateTime.UtcNow, DateTime.UtcNow);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task A_reachable_discovery_document_reports_its_contents()
    {
        var tester = TesterFor(_ => Json("""
            {
              "issuer": "https://idp.example.com/realms/auton8",
              "authorization_endpoint": "https://idp.example.com/auth",
              "token_endpoint": "https://idp.example.com/token",
              "jwks_uri": "https://idp.example.com/certs"
            }
            """));

        var result = await tester.TestAsync(Oidc(), CancellationToken.None);

        Assert.True(result.Success, result.Summary);
        Assert.Contains("issuer: https://idp.example.com/realms/auton8", result.Findings);
        Assert.Contains(result.Findings, f => f.StartsWith("jwks_uri:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_discovery_document_missing_a_required_field_is_reported_not_accepted()
    {
        // Reachable but unusable is its own outcome, and the one an
        // administrator most needs spelled out.
        var tester = TesterFor(_ => Json("""{"issuer": "https://idp.example.com/realms/auton8"}"""));

        var result = await tester.TestAsync(Oidc(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Findings, f => f.Contains("MISSING token_endpoint", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("MISSING jwks_uri", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_provider_reports_a_failure_rather_than_throwing()
    {
        var tester = TesterFor(_ => throw new HttpRequestException("Name or service not known"));

        var result = await tester.TestAsync(Oidc(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Could not reach", result.Summary, StringComparison.Ordinal);
        Assert.Contains("Name or service not known", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_json_response_says_what_is_probably_wrong()
    {
        // The common mistake is pasting the login page URL instead of the
        // issuer, which returns HTML with a 200. "Invalid JSON" would be true
        // and useless; the message names the likely cause.
        var tester = TesterFor(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!doctype html><title>Sign in</title>", Encoding.UTF8, "text/html"),
        });

        var result = await tester.TestAsync(Oidc(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not the login page", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_non_success_status_is_reported_with_the_code()
    {
        var tester = TesterFor(_ => Json("{}", HttpStatusCode.NotFound));

        var result = await tester.TestAsync(Oidc(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("404", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_outside_the_allowlist_is_refused_before_any_request_is_made()
    {
        var called = false;
        var tester = TesterFor(_ =>
        {
            called = true;
            return Json("{}");
        });

        var result = await tester.TestAsync(Oidc("https://evil.example.com"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(called, "The allowlist must be enforced before the request, not after.");
        Assert.Contains("not an allowed endpoint", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_authority_that_already_includes_the_well_known_path_is_not_doubled()
    {
        // Administrators paste both forms and the difference is not obvious
        // from an IdP's own console.
        Uri? requested = null;
        var tester = TesterFor(req =>
        {
            requested = req.RequestUri;
            return Json("""{"issuer":"i","authorization_endpoint":"a","token_endpoint":"t","jwks_uri":"j"}""");
        });

        await tester.TestAsync(
            Oidc($"https://{Host}/realms/auton8/.well-known/openid-configuration"),
            CancellationToken.None);

        Assert.Equal(
            $"https://{Host}/realms/auton8/.well-known/openid-configuration",
            requested!.ToString());
    }

    [Fact]
    public async Task Saml_metadata_without_an_IDPSSODescriptor_is_reported()
    {
        var tester = TesterFor(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<EntityDescriptor><SPSSODescriptor/></EntityDescriptor>",
                Encoding.UTF8, "application/xml"),
        });

        var result = await tester.TestAsync(Saml($"https://{Host}/metadata"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Findings, f => f.Contains("MISSING IDPSSODescriptor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_saml_metadata_needs_no_network_call()
    {
        var called = false;
        var tester = TesterFor(_ => { called = true; return Json("{}"); });

        var result = await tester.TestAsync(Saml(metadataUrl: null, inlineXml: true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(called, "There is nothing to fetch when metadata is stored inline.");
    }

    [Fact]
    public async Task Saml_with_neither_a_url_nor_inline_metadata_says_so()
    {
        var tester = TesterFor(_ => Json("{}"));

        var result = await tester.TestAsync(Saml(metadataUrl: null, inlineXml: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Provide one of them", result.Summary, StringComparison.Ordinal);
    }

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
