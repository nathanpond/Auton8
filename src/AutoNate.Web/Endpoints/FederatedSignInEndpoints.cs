using System.Security.Claims;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AutoNate.Web.Endpoints;

/// <summary>
/// The federated sign-in routes: what the login page lists, and the OIDC
/// challenge/callback pair.
/// </summary>
/// <remarks>
/// These are deliberately anonymous — they are how someone who is not yet
/// authenticated becomes authenticated. Everything they expose is either public
/// by nature (a provider's display name, which appears on a button) or bound to
/// a one-time state value this server issued.
/// </remarks>
public static class FederatedSignInEndpoints
{
    // Short-lived, HttpOnly, SameSite=Lax cookies carrying the challenge state.
    // Lax rather than Strict: the IdP redirects the browser back with a
    // top-level GET, and Strict would withhold the cookie on exactly that
    // navigation, breaking every sign-in.
    private const string StateCookie = "auton8_oidc_state";
    private const string VerifierCookie = "auton8_oidc_verifier";
    private const string NonceCookie = "auton8_oidc_nonce";
    private const string ReturnCookie = "auton8_oidc_return";

    public static IEndpointRouteBuilder MapFederatedSignInEndpoints(this IEndpointRouteBuilder app)
    {
        // What the login page needs to draw buttons, and nothing else. Not the
        // authority, not the client id, and certainly not the secret — a
        // signed-out visitor gets display name, kind and slug.
        app.MapGet("/api/auth/providers", async (
            IIdentityProviderStore store, CancellationToken ct) =>
        {
            var providers = await store.ListAsync(ct);
            return Results.Ok(providers
                .Where(p => p.IsEnabled)
                .Select(p => new { p.Slug, p.DisplayName, p.Kind })
                .ToList());
        }).AllowAnonymous();

        app.MapGet("/api/auth/oidc/{slug}/challenge", async (
            string slug,
            string? returnUrl,
            HttpContext http,
            IOidcSignInService oidc,
            CancellationToken ct) =>
        {
            var callback = CallbackUri(http, slug);
            var challenge = await oidc.BuildChallengeAsync(slug, callback, ct);
            if (challenge is null)
            {
                // Covers both "no such provider" and "disabled" on purpose: an
                // anonymous caller should not be able to enumerate which
                // providers exist but are switched off.
                return Results.Redirect("/?error=provider_unavailable");
            }

            var options = CookieOptions(http);
            http.Response.Cookies.Append(StateCookie, challenge.State, options);
            http.Response.Cookies.Append(VerifierCookie, challenge.CodeVerifier, options);
            http.Response.Cookies.Append(NonceCookie, challenge.Nonce, options);
            http.Response.Cookies.Append(ReturnCookie, SafeReturn(returnUrl), options);

            return Results.Redirect(challenge.RedirectUri);
        }).AllowAnonymous();

        app.MapGet("/api/auth/oidc/{slug}/callback", async (
            string slug,
            string? code,
            string? state,
            string? error,
            HttpContext http,
            IOidcSignInService oidc,
            IAuditEventPublisher audit,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("AutoNate.Web.Endpoints.FederatedSignInEndpoints");

            var expectedState = http.Request.Cookies[StateCookie];
            var verifier = http.Request.Cookies[VerifierCookie];
            var nonce = http.Request.Cookies[NonceCookie];
            var returnUrl = http.Request.Cookies[ReturnCookie];
            ClearChallengeCookies(http);

            if (!string.IsNullOrEmpty(error))
            {
                // The IdP itself refused. Audited as a failed login because from
                // the product's point of view that is what happened.
                await PublishFailureAsync(audit, slug, $"idp_error:{error}", ct);
                log.LogWarning("OIDC provider {Slug} returned error '{Error}'.", slug, error);
                return Results.Redirect("/?error=invalid");
            }

            if (string.IsNullOrEmpty(code))
            {
                await PublishFailureAsync(audit, slug, "missing_code", ct);
                return Results.Redirect("/?error=invalid");
            }

            var result = await oidc.CompleteAsync(
                slug, code, state ?? string.Empty, expectedState ?? string.Empty,
                verifier ?? string.Empty, nonce ?? string.Empty, CallbackUri(http, slug), ct);

            if (!result.Succeeded || result.User is null)
            {
                await PublishFailureAsync(audit, slug, result.Reason.ToString(), ct);
                return Results.Redirect("/?error=invalid");
            }

            // JIT creation is audited with the same event the manual
            // user-creation endpoint emits, so an account arriving through
            // federation is on the record like any other.
            if (result.AccountCreated)
            {
                await audit.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.UserCreated,
                    IamResourceKinds.User,
                    resource: new { userId = result.User.UserId, username = result.User.Username },
                    details: new { createdBy = "federated-sign-in", provider = slug, roleAssignments = 0 },
                    ct);
            }

            // The same principal construction the local and dev-auto-login paths
            // use, so the session shape and everything authorization reads from
            // it are identical rather than parallel.
            var principal = PrincipalFactory.Build(result.User, $"oidc:{slug}");
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true,
                    IssuedUtc = DateTimeOffset.UtcNow,
                });

            await audit.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.LoginSucceeded,
                AuthEventTopic.ResourceKind,
                resource: new { userId = result.User.UserId, username = result.User.Username },
                details: new { authSource = $"oidc:{slug}", provider = slug, accountCreated = result.AccountCreated },
                ct);

            return Results.Redirect(SafeReturn(returnUrl));
        }).AllowAnonymous();

        return app;
    }

    private static Task PublishFailureAsync(
        IAuditEventPublisher audit, string slug, string reason, CancellationToken ct) =>
        audit.PublishAsync(
            AuthEventTopic.TopicName,
            AuthEventTypes.LoginFailed,
            AuthEventTopic.ResourceKind,
            resource: new { provider = slug },
            // The reason is in the audit trail as well as the log: an auditor
            // asking "how did this account get in" should not need log access.
            details: new { authSource = $"oidc:{slug}", provider = slug, reason },
            ct);

    private static string CallbackUri(HttpContext http, string slug) =>
        $"{http.Request.Scheme}://{http.Request.Host}/api/auth/oidc/{Uri.EscapeDataString(slug)}/callback";

    private static CookieOptions CookieOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        // Lax, not Strict: the IdP returns the browser here with a top-level
        // GET, and Strict withholds the cookie on precisely that navigation.
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        // The window between challenge and callback is a user authenticating at
        // their IdP. Ten minutes is generous for that and short enough that a
        // stale tab fails cleanly rather than replaying much later.
        Expires = DateTimeOffset.UtcNow.AddMinutes(10),
        Path = "/",
    };

    private static void ClearChallengeCookies(HttpContext http)
    {
        foreach (var name in new[] { StateCookie, VerifierCookie, NonceCookie, ReturnCookie })
        {
            http.Response.Cookies.Delete(name);
        }
    }

    /// <summary>Local paths only — an open redirect here would be a phishing gift.</summary>
    private static string SafeReturn(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/home";
}

/// <summary>
/// Builds the cookie principal for a signed-in user.
/// </summary>
/// <remarks>
/// Program.cs has a local <c>BuildPrincipal</c> that the local and
/// dev-auto-login paths use. This mirrors it for the federated path rather than
/// making that one public, because a top-level statement file's local function
/// cannot be shared — and the shapes must not drift, which
/// FederatedSignInTests asserts by comparing the claim types both produce.
/// </remarks>
public static class PrincipalFactory
{
    public const string AuthenticationSourceClaimType = "auth_source";

    public static ClaimsPrincipal Build(LocalUser user, string authenticationSource)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(AuthenticationSourceClaimType, authenticationSource),
        };

        if (!string.IsNullOrWhiteSpace(user.IdpKey))
        {
            claims.Add(new Claim("idp_key", user.IdpKey));
        }

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
