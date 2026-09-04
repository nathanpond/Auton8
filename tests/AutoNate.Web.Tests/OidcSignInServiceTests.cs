using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The OIDC code flow against a stub identity provider that really signs tokens.
/// </summary>
/// <remarks>
/// #90's test plan asks for a stub "real enough to sign tokens", and that is the
/// only way these tests mean anything: a stub that returns a canned success
/// would exercise none of the validation, which is the entire security surface
/// of this story.
///
/// So the stub holds an RSA key, publishes a JWKS, and mints id_tokens. Every
/// rejection test below produces a token that is *correct in every respect
/// except one* — which is what makes each of them a test of the specific check
/// rather than of the parser.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class OidcSignInServiceTests
{
    private const string Authority = "https://idp.example.com";
    private const string ClientId = "auton8";
    private const string Slug = "corp";
    private const string Subject = "sub-12345";
    private const string CallbackUri = "https://app.example.com/api/auth/oidc/corp/callback";

    // ── The stub IdP ────────────────────────────────────────────────────────

    private sealed class StubIdp
    {
        public RsaSecurityKey Key { get; }
        private readonly string _kid = "test-key-1";

        public StubIdp()
        {
            Key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = _kid };
        }

        public string Discovery() => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["issuer"] = Authority,
            ["authorization_endpoint"] = $"{Authority}/authorize",
            ["token_endpoint"] = $"{Authority}/token",
            ["jwks_uri"] = $"{Authority}/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
        });

        public string Jwks()
        {
            var parameters = Key.Rsa.ExportParameters(false);
            return JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = _kid,
                        alg = "RS256",
                        n = Base64Url(parameters.Modulus!),
                        e = Base64Url(parameters.Exponent!),
                    }
                }
            });
        }

        public string MintToken(
            string? nonce,
            string? subject = Subject,
            string issuer = Authority,
            string audience = ClientId,
            DateTime? expires = null,
            SecurityKey? signingKey = null)
        {
            var claims = new List<Claim>
            {
                new("sub", subject ?? string.Empty),
                new("email", "someone@example.com"),
                new("preferred_username", "someone"),
                new("given_name", "Some"),
                new("family_name", "One"),
            };
            if (subject is null) claims.RemoveAll(c => c.Type == "sub");
            if (nonce is not null) claims.Add(new Claim("nonce", nonce));

            var credentials = new SigningCredentials(
                signingKey ?? Key, SecurityAlgorithms.RsaSha256);

            // notBefore is derived from the expiry rather than from "now":
            // an expired-token test sets expires into the past, and a token
            // whose notBefore is after its expiry is rejected by the
            // constructor — which would make the test assert a code-exchange
            // failure instead of the validation failure it is about.
            var expiry = expires ?? DateTime.UtcNow.AddMinutes(5);
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: expiry.AddMinutes(-10),
                expires: expiry,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string Base64Url(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class StubHandler(StubIdp idp, Func<string>? tokenFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("openid-configuration", StringComparison.Ordinal))
                return Task.FromResult(Json(idp.Discovery()));
            if (url.EndsWith("/jwks", StringComparison.Ordinal))
                return Task.FromResult(Json(idp.Jwks()));
            if (url.EndsWith("/token", StringComparison.Ordinal))
            {
                if (tokenFactory is null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"error\":\"invalid_grant\"}"),
                    });
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    access_token = "at",
                    token_type = "Bearer",
                    id_token = tokenFactory(),
                })));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class StubFactory(StubIdp idp, Func<string>? tokenFactory) : IHttpClientFactory
    {
        /// <summary>
        /// Settable so a test can re-mint against the nonce the server issued.
        /// </summary>
        /// <remarks>
        /// The endpoint-level test cannot know the nonce before the challenge
        /// runs, and fabricating one to match would skip the nonce check —
        /// which is a real defence, and skipping it silently is how a test
        /// stops covering what it claims to.
        /// </remarks>
        public Func<string>? TokenFactory { get; set; } = tokenFactory;

        public HttpClient CreateClient(string name) => new(new StubHandler(idp, TokenFactory));
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static async Task<(OidcSignInService Service, AutoNateWebApplicationFactory App, Guid ProviderId)>
        BuildAsync(StubIdp idp, Func<string>? tokenFactory)
    {
        var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();

        var provider = await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Oidc,
            DisplayName: "Corporate",
            Slug: Slug,
            IsEnabled: true,
            OidcAuthority: Authority,
            OidcClientId: ClientId,
            OidcScopes: null,
            SamlEntityId: null, SamlMetadataUrl: null, SamlMetadataXml: null,
            SamlSigningCertificate: null,
            Secret: "client-secret"), Guid.NewGuid(), CancellationToken.None);

        // A cache per test, which is the point of it being a service: each
        // StubIdp has its own signing key, and a shared cache would hand one
        // test's keys to the next.
        var httpFactory = new StubFactory(idp, tokenFactory);
        var service = new OidcSignInService(
            store,
            app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>(),
            httpFactory,
            new OidcConfigurationCache(httpFactory),
            NullLogger<OidcSignInService>.Instance);

        return (service, app, provider.Id);
    }

    private static Task<OidcSignInResult> CompleteAsync(
        OidcSignInService service, string state, string expectedState, string nonce, string expectedNonce) =>
        service.CompleteAsync(Slug, "the-code", state, expectedState, "verifier", expectedNonce,
            CallbackUri, CancellationToken.None);

    // ── The happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_valid_code_flow_creates_an_account_with_no_roles()
    {
        var idp = new StubIdp();
        const string Nonce = "nonce-abc";
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken(Nonce));
        await using var _app = app;

        var result = await CompleteAsync(service, "st", "st", Nonce, Nonce);

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.True(result.AccountCreated);
        Assert.NotNull(result.User);
        Assert.Equal($"{Slug}:{Subject}", result.User!.IdpKey);

        // The criterion this story exists to protect: a first federated sign-in
        // grants nothing. Asserted against the database, not inferred.
        var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var assignments = await db.RoleAssignments
            .CountAsync(a => a.PrincipalId == result.User.UserId.ToString());
        Assert.Equal(0, assignments);
    }

    [Fact]
    public async Task A_returning_user_is_matched_on_the_subject_not_the_email()
    {
        var idp = new StubIdp();
        const string Nonce = "n";
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken(Nonce));
        await using var _app = app;

        var first = await CompleteAsync(service, "s", "s", Nonce, Nonce);
        Assert.True(first.AccountCreated);

        var second = await CompleteAsync(service, "s", "s", Nonce, Nonce);
        Assert.True(second.Succeeded);
        Assert.False(second.AccountCreated);
        Assert.Equal(first.User!.UserId, second.User!.UserId);
    }

    [Fact]
    public async Task A_federated_account_has_no_local_password()
    {
        // Empty hash and salt rather than a hash of something: there is no
        // plaintext that produces this, so the local password path cannot
        // authenticate the account even by accident.
        var idp = new StubIdp();
        const string Nonce = "n";
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken(Nonce));
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", Nonce, Nonce);

        var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.LocalUsers.SingleAsync(u => u.UserId == result.User!.UserId);
        Assert.Equal(string.Empty, row.PasswordHash);
        Assert.Equal(string.Empty, row.PasswordSalt);
    }

    // ── Rejection paths, each failing differently ───────────────────────────

    [Fact]
    public async Task A_mismatched_state_is_rejected_as_a_state_mismatch()
    {
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken("n"));
        await using var _app = app;

        var result = await CompleteAsync(service, "returned", "expected-something-else", "n", "n");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.StateMismatch, result.Reason);
    }

    [Fact]
    public async Task A_mismatched_nonce_is_rejected_as_a_nonce_mismatch()
    {
        // A correctly signed, unexpired, correctly addressed token — whose nonce
        // belongs to a different challenge. That is a replay, and it must not
        // read as a signature problem.
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken("nonce-from-another-login"));
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", "n", "the-nonce-we-issued");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.NonceMismatch, result.Reason);
    }

    [Fact]
    public async Task An_expired_token_is_rejected_as_a_validation_failure()
    {
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp,
            () => idp.MintToken("n", expires: DateTime.UtcNow.AddMinutes(-30)));
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", "n", "n");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.TokenValidationFailed, result.Reason);
        Assert.Contains("expired", result.Diagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_token_signed_by_the_wrong_key_is_rejected()
    {
        // The one that matters most: a token that is perfect except that
        // somebody else signed it.
        var idp = new StubIdp();
        var attacker = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key-1" };
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken("n", signingKey: attacker));
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", "n", "n");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.TokenValidationFailed, result.Reason);
    }

    [Fact]
    public async Task A_token_for_a_different_audience_is_rejected()
    {
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken("n", audience: "some-other-client"));
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", "n", "n");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.TokenValidationFailed, result.Reason);
    }

    [Fact]
    public async Task A_token_with_no_subject_is_rejected_as_a_missing_subject()
    {
        // Distinct from a validation failure on purpose: the token is genuine,
        // it just carries nothing stable to key an account on. That is a
        // provider misconfiguration, and an administrator needs to be told
        // which of the two it is.
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken("n", subject: null));
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", "n", "n");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.SubjectMissing, result.Reason);
    }

    [Fact]
    public async Task A_failed_code_exchange_is_rejected_as_a_code_exchange_failure()
    {
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp, tokenFactory: null);
        await using var _app = app;

        var result = await CompleteAsync(service, "s", "s", "n", "n");

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.CodeExchangeFailed, result.Reason);
    }

    [Fact]
    public async Task A_disabled_provider_cannot_be_used_to_sign_in()
    {
        var idp = new StubIdp();
        const string Nonce = "n";
        var (service, app, providerId) = await BuildAsync(idp, () => idp.MintToken(Nonce));
        await using var _app = app;

        var store = app.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        await store.SetEnabledAsync(providerId, false, Guid.NewGuid(), CancellationToken.None);

        var result = await CompleteAsync(service, "s", "s", Nonce, Nonce);

        Assert.False(result.Succeeded);
        Assert.Equal(OidcFailureReason.ProviderNotFound, result.Reason);
    }

    [Fact]
    public void Every_rejection_reason_is_distinguishable()
    {
        // The AC is that the six causes produce different messages. Asserting
        // the enum has distinct members is the cheap half; the tests above are
        // the half that proves each path reaches the right one.
        var values = Enum.GetValues<OidcFailureReason>().Where(v => v != OidcFailureReason.None).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.True(values.Count >= 6, "There should be a distinct reason per rejection path.");
    }

    [Fact]
    public async Task A_challenge_uses_PKCE_with_S256_and_carries_state_and_nonce()
    {
        var idp = new StubIdp();
        var (service, app, _) = await BuildAsync(idp, () => idp.MintToken("n"));
        await using var _app = app;

        var challenge = await service.BuildChallengeAsync(Slug, CallbackUri, CancellationToken.None);

        Assert.NotNull(challenge);
        Assert.Contains("code_challenge_method=S256", challenge!.RedirectUri, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", challenge.RedirectUri, StringComparison.Ordinal);
        Assert.Contains("response_type=code", challenge.RedirectUri, StringComparison.Ordinal);
        Assert.Contains($"state={challenge.State}", challenge.RedirectUri, StringComparison.Ordinal);
        Assert.Contains($"nonce={challenge.Nonce}", challenge.RedirectUri, StringComparison.Ordinal);

        // The verifier must never be in the redirect — only its hash.
        Assert.DoesNotContain(challenge.CodeVerifier, challenge.RedirectUri, StringComparison.Ordinal);
    }
    // ── The session has to survive the request after the callback ───────────

    [Fact]
    public async Task An_oidc_session_still_authenticates_on_the_next_request()
    {
        // The assertion this suite was missing (#139), and the one that matters:
        // not "did CompleteAsync succeed" or "was SignInAsync called", but "is
        // the user actually signed in on the request after".
        //
        // Every other test here calls CompleteAsync directly, so none of them
        // goes through the HTTP pipeline — which is where the principal is
        // turned into a cookie and where the middleware that broke this lives.
        // Federated sign-in was unusable in Development for as long as it
        // existed and this suite stayed green throughout.
        var idp = new StubIdp();
        const string Nonce = "nonce-endpoint";

        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        var httpFactory = new StubFactory(idp, () => idp.MintToken(Nonce));

        // The stub reaches the app's own DI, so the callback endpoint runs the
        // real service against a real signed token rather than a fake service.
        using var scoped = app.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton<IHttpClientFactory>(httpFactory);
            services.AddSingleton<IOidcConfigurationCache>(new OidcConfigurationCache(httpFactory));
        }));

        var client = scoped.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
            .WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var store = scoped.Services.CreateScope().ServiceProvider
            .GetRequiredService<IIdentityProviderStore>();
        await store.CreateAsync(new CreateIdentityProviderRequest(
            Kind: IdentityProviderKinds.Oidc,
            DisplayName: "Corporate",
            Slug: Slug,
            IsEnabled: true,
            OidcAuthority: Authority,
            OidcClientId: ClientId,
            OidcScopes: null,
            SamlEntityId: null, SamlMetadataUrl: null, SamlMetadataXml: null,
            SamlSigningCertificate: null,
            Secret: "client-secret"), Guid.NewGuid(), CancellationToken.None);

        // Drive the real challenge so the state/verifier/nonce cookies are the
        // ones the server issued, rather than values fabricated to match.
        var challenge = await client.GetAsync($"/api/auth/oidc/{Slug}/challenge?returnUrl=%2Fhome");
        var challengeCookies = challenge.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .ToList();
        var state = challengeCookies
            .First(c => c.StartsWith("auton8_oidc_state=", StringComparison.Ordinal))
            .Split('=')[1];

        // The stub mints a token carrying this nonce, so the challenge's nonce
        // cookie has to be the same value or the callback rejects it — which is
        // correct behaviour and would mask the thing under test.
        var nonceCookie = challengeCookies
            .First(c => c.StartsWith("auton8_oidc_nonce=", StringComparison.Ordinal));
        var issuedNonce = nonceCookie.Split('=')[1];

        var callback = new HttpRequestMessage(
            HttpMethod.Get, $"/api/auth/oidc/{Slug}/callback?code=the-code&state={state}");
        foreach (var c in challengeCookies)
        {
            callback.Headers.Add("Cookie", c.StartsWith("auton8_oidc_nonce=", StringComparison.Ordinal)
                ? $"auton8_oidc_nonce={issuedNonce}"
                : c);
        }

        // Re-mint against the nonce the server actually issued.
        httpFactory.TokenFactory = () => idp.MintToken(issuedNonce);

        var response = await client.SendAsync(callback);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("error=", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie")
                .Where(c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)));

        var second = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        second.Headers.Add("Cookie", cookie.Split(';')[0]);
        var me = await client.SendAsync(second);

        me.EnsureSuccessStatusCode();
        var body = await me.Content.ReadAsStringAsync();

        // Naming WHO, not merely that somebody is signed in. "authenticated:
        // true" passes against the defect, because Development auto-login
        // immediately signs the request back in as `admin` — the federated
        // session is destroyed and replaced, and the weaker assertion cannot
        // tell the difference.
        Assert.Contains($"\"authSource\":\"oidc:{Slug}\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"idpKey\":\"{Slug}:{Subject}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"username\":\"admin\"", body, StringComparison.Ordinal);
    }

}
