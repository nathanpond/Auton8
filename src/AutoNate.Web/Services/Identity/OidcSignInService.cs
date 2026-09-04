using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace AutoNate.Web.Services.Identity;

/// <summary>
/// Why each rejection happened. The user always sees a generic failure; this is
/// what goes in the log so an administrator can tell the six apart.
/// </summary>
/// <remarks>
/// #90 is explicit that a single "authentication failed" for every cause is what
/// makes these unmaintainable in production. Provider unreachable, a signature
/// failure and a missing subject claim are three different operational problems
/// with three different fixes.
/// </remarks>
public enum OidcFailureReason
{
    None = 0,
    ProviderNotFound,
    ProviderDisabled,
    ProviderUnreachable,
    StateMismatch,
    CodeExchangeFailed,
    TokenValidationFailed,
    NonceMismatch,
    SubjectMissing,
}

public sealed record OidcChallenge(string RedirectUri, string State, string CodeVerifier, string Nonce);

public sealed record OidcSignInResult(
    bool Succeeded,
    LocalUser? User,
    bool AccountCreated,
    OidcFailureReason Reason,
    string? Diagnostic);

public interface IOidcSignInService
{
    Task<OidcChallenge?> BuildChallengeAsync(string slug, string callbackUri, CancellationToken ct);

    Task<OidcSignInResult> CompleteAsync(
        string slug, string code, string returnedState, string expectedState,
        string codeVerifier, string expectedNonce, string callbackUri, CancellationToken ct);
}

/// <summary>
/// The OIDC authorization-code flow, driven from database-stored providers.
/// </summary>
/// <remarks>
/// #95 asked for a deliberate choice between dynamic scheme registration and
/// implementing the flow. This implements it, because providers here are created
/// and edited at runtime and there can be several — a scheme registry would have
/// to be kept in sync across instances, and a provider edited on one instance is
/// unknown to another until its options cache expires.
///
/// The split matters though: this class owns the *flow*, and
/// <see cref="Microsoft.IdentityModel"/> owns the *cryptography*. Discovery,
/// JWKS retrieval and caching, and signature/issuer/audience/lifetime validation
/// all go through the library. Hand-rolling a redirect is fine; hand-rolling JWT
/// signature validation is how a security hole gets shipped.
/// </remarks>
public sealed class OidcSignInService : IOidcSignInService
{
    private readonly IIdentityProviderStore _providers;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OidcSignInService> _log;

    private readonly IOidcConfigurationCache _configurations;

    public OidcSignInService(
        IIdentityProviderStore providers,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        IOidcConfigurationCache configurations,
        ILogger<OidcSignInService> log)
    {
        _providers = providers;
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
        _configurations = configurations;
        _log = log;
    }

    public async Task<OidcChallenge?> BuildChallengeAsync(string slug, string callbackUri, CancellationToken ct)
    {
        var provider = await FindEnabledAsync(slug, ct);
        if (provider is null) return null;

        OpenIdConnectConfiguration config;
        try
        {
            config = await GetConfigurationAsync(provider, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "OIDC challenge failed for provider {Slug}: the discovery document could not be retrieved.", slug);
            return null;
        }

        // PKCE S256. The verifier never leaves this server; only its hash does,
        // so an intercepted authorization code cannot be redeemed by anyone else.
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var scopes = string.IsNullOrWhiteSpace(provider.OidcScopes)
            ? "openid profile email"
            : provider.OidcScopes!;

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = provider.OidcClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = callbackUri,
            ["scope"] = scopes,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };

        var url = QueryHelpers(config.AuthorizationEndpoint, query);
        return new OidcChallenge(url, state, verifier, nonce);
    }

    public async Task<OidcSignInResult> CompleteAsync(
        string slug, string code, string returnedState, string expectedState,
        string codeVerifier, string expectedNonce, string callbackUri, CancellationToken ct)
    {
        var provider = await FindEnabledAsync(slug, ct);
        if (provider is null)
        {
            return Fail(OidcFailureReason.ProviderNotFound,
                $"No enabled provider with slug '{slug}'.");
        }

        // Checked before anything else and with a fixed-time comparison: state is
        // the CSRF defence, and a variable-time compare on it is a real, if
        // narrow, oracle.
        if (string.IsNullOrEmpty(expectedState)
            || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(returnedState ?? string.Empty),
                    Encoding.UTF8.GetBytes(expectedState)))
        {
            return Fail(OidcFailureReason.StateMismatch,
                "The state parameter did not match the value issued with the challenge. "
                + "This is either a cross-site request forgery attempt or a stale login tab.");
        }

        OpenIdConnectConfiguration config;
        try
        {
            config = await GetConfigurationAsync(provider, ct);
        }
        catch (Exception ex)
        {
            return Fail(OidcFailureReason.ProviderUnreachable,
                $"The provider's discovery document could not be retrieved: {ex.Message}");
        }

        string idToken;
        try
        {
            idToken = await ExchangeCodeAsync(provider, config, code, codeVerifier, callbackUri, ct);
        }
        catch (Exception ex)
        {
            return Fail(OidcFailureReason.CodeExchangeFailed,
                $"The authorization code could not be exchanged at the token endpoint: {ex.Message}");
        }

        JwtSecurityToken jwt;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,
                ValidateAudience = true,
                ValidAudience = provider.OidcClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                // No leeway beyond the default: a token minted for five minutes
                // should not be accepted for ten.
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            handler.ValidateToken(idToken, parameters, out var validated);
            jwt = (JwtSecurityToken)validated;
        }
        catch (SecurityTokenExpiredException ex)
        {
            return Fail(OidcFailureReason.TokenValidationFailed, $"The id_token has expired: {ex.Message}");
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            return Fail(OidcFailureReason.TokenValidationFailed,
                $"The id_token signature did not validate against the provider's published keys: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Fail(OidcFailureReason.TokenValidationFailed, $"The id_token failed validation: {ex.Message}");
        }

        // Nonce binds this token to the challenge this browser started. Checked
        // after signature validation, because an unsigned token's claims mean
        // nothing.
        var nonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (string.IsNullOrEmpty(expectedNonce) || !string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            return Fail(OidcFailureReason.NonceMismatch,
                "The id_token's nonce did not match the value issued with the challenge — "
                + "the token may be a replay of an earlier authentication.");
        }

        var subject = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Fail(OidcFailureReason.SubjectMissing,
                "The id_token carried no 'sub' claim, so there is no stable identifier to key the account on.");
        }

        var (user, created) = await MapToLocalUserAsync(provider, subject!, jwt, ct);
        return new OidcSignInResult(true, user, created, OidcFailureReason.None, null);
    }

    /// <summary>
    /// Finds or creates the local account for an authenticated subject.
    /// </summary>
    /// <remarks>
    /// Keyed on <c>idp_key</c> = <c>{slug}:{sub}</c>, never on email. #90 is
    /// explicit about why: an email change at the IdP must not create a second
    /// account, and an email *reassignment* must not let a new person take over
    /// an existing one. The slug prefix means the same subject at two providers
    /// is two accounts, which is the correct reading of a subject being unique
    /// only within its issuer.
    ///
    /// A first-time user is created with **no role assignments**. That is a
    /// planning decision, not an oversight: this project has already shipped one
    /// accidental bulk grant, and claim-driven privilege on first contact is the
    /// same defect wearing different clothes. Group mapping is #92.
    /// </remarks>
    private async Task<(LocalUser User, bool Created)> MapToLocalUserAsync(
        IdentityProviderDto provider, string subject, JwtSecurityToken jwt, CancellationToken ct)
    {
        var idpKey = $"{provider.Slug}:{subject}";

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.LocalUsers.FirstOrDefaultAsync(u => u.IdpKey == idpKey, ct);
        if (existing is not null)
        {
            existing.LastLoginDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return (PersistenceModelMapper.ToModel(existing), false);
        }

        var email = Claim(jwt, "email") ?? $"{subject}@{provider.Slug}.invalid";
        var username = Claim(jwt, "preferred_username") ?? email;

        var row = new Persistence.Scaffolded.LocalUser
        {
            UserId = Guid.NewGuid(),
            Username = await UniqueUsernameAsync(db, username, ct),
            Email = email,
            FirstName = Claim(jwt, "given_name") ?? string.Empty,
            LastName = Claim(jwt, "family_name") ?? string.Empty,
            IdpKey = idpKey,
            // A federated account has no local password, and these columns are
            // NOT NULL. Empty rather than a hash of something: there is no
            // plaintext that produces this, so the local password path cannot
            // authenticate it even by accident.
            PasswordHash = string.Empty,
            PasswordSalt = string.Empty,
            CreatedDate = DateTime.UtcNow,
            LastLoginDate = DateTime.UtcNow,
            FailedLoginAttempts = 0,
            IsLocked = false,
        };

        db.LocalUsers.Add(row);
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Created a federated account for provider {Slug} subject {Subject} with no role assignments.",
            provider.Slug, subject);

        return (PersistenceModelMapper.ToModel(row), true);
    }

    private static async Task<string> UniqueUsernameAsync(AutoNateDbContext db, string desired, CancellationToken ct)
    {
        var candidate = desired;
        var suffix = 1;
        while (await db.LocalUsers.AnyAsync(u => u.Username == candidate, ct))
        {
            candidate = $"{desired}-{++suffix}";
        }
        return candidate;
    }

    private static string? Claim(JwtSecurityToken jwt, string type) =>
        jwt.Claims.FirstOrDefault(c => c.Type == type)?.Value is { Length: > 0 } v ? v : null;

    private async Task<string> ExchangeCodeAsync(
        IdentityProviderDto provider, OpenIdConnectConfiguration config,
        string code, string codeVerifier, string callbackUri, CancellationToken ct)
    {
        var secret = await _providers.RevealSecretAsync(provider.Id, ct);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUri,
            ["client_id"] = provider.OidcClientId ?? string.Empty,
            ["code_verifier"] = codeVerifier,
        };
        if (!string.IsNullOrEmpty(secret)) form["client_secret"] = secret;

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        using var response = await client.PostAsync(
            config.TokenEndpoint, new FormUrlEncodedContent(form), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("id_token", out var idToken))
        {
            throw new InvalidOperationException("The token response carried no id_token.");
        }
        return idToken.GetString() ?? throw new InvalidOperationException("The id_token was null.");
    }

    private async Task<IdentityProviderDto?> FindEnabledAsync(string slug, CancellationToken ct)
    {
        var all = await _providers.ListAsync(ct);
        return all.FirstOrDefault(p =>
            p.IsEnabled
            && p.Kind == IdentityProviderKinds.Oidc
            && string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    private Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        IdentityProviderDto provider, CancellationToken ct)
    {
        var authority = (provider.OidcAuthority ?? string.Empty).TrimEnd('/');
        var metadata = authority.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase)
            ? authority
            : authority + "/.well-known/openid-configuration";
        return _configurations.GetAsync(metadata, ct);
    }

    private OidcSignInResult Fail(OidcFailureReason reason, string diagnostic)
    {
        // Logged with the reason named; the caller shows the user something
        // generic. An administrator reading logs can tell the six causes apart,
        // which is the whole point of the enum.
        _log.LogWarning("OIDC sign-in rejected ({Reason}): {Diagnostic}", reason, diagnostic);
        return new OidcSignInResult(false, null, false, reason, diagnostic);
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string QueryHelpers(string baseUri, Dictionary<string, string?> query)
    {
        var parts = query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        var separator = baseUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return baseUri + separator + string.Join("&", parts);
    }
}
