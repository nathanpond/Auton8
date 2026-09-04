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
/// The federated sign-in routes: what the login page lists, the OIDC
/// challenge/callback pair, and the SAML metadata/challenge/ACS trio.
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
            IIdentityProviderStore store, ISignInMethodPolicy policy, CancellationToken ct) =>
        {
            var methods = await policy.GetAsync(ct);
            var providers = await store.ListAsync(ct);

            // Filtered by method, not merely listed: a provider whose whole
            // protocol is switched off must not appear, or the login page draws
            // a button whose endpoint will refuse it.
            return Results.Ok(providers
                .Where(p => p.IsEnabled && MethodEnabled(methods, p.Kind))
                .Select(p => new { p.Slug, p.DisplayName, p.Kind })
                .ToList());
        }).AllowAnonymous();

        // What the login page needs to decide which forms to draw. Deliberately
        // only the three booleans and nothing about how they were arrived at:
        // whether the break-glass override is what is keeping local available
        // is an operational fact, and a signed-out visitor learning it would
        // learn that the install is currently in trouble.
        app.MapGet("/api/auth/methods", async (ISignInMethodPolicy policy, CancellationToken ct) =>
        {
            var methods = await policy.GetAsync(ct);
            return Results.Ok(new { methods.Local, methods.Oidc, methods.Saml });
        }).AllowAnonymous();

        app.MapGet("/api/auth/oidc/{slug}/challenge", async (
            string slug,
            string? returnUrl,
            HttpContext http,
            IOidcSignInService oidc,
            ISignInMethodPolicy policy,
            CancellationToken ct) =>
        {
            if (!(await policy.GetAsync(ct)).Oidc)
            {
                return Results.Redirect("/?error=provider_unavailable");
            }

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
            IIdentityProviderStore store,
            IAuditEventPublisher audit,
            IClaimGroupReconciler reconciler,
            ISignInMethodPolicy policy,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("AutoNate.Web.Endpoints.FederatedSignInEndpoints");

            // Checked on the callback as well as the challenge. A tab that
            // started a sign-in before the method was switched off must not be
            // able to finish one after — the window is small, and "small" is not
            // a property anyone should have to rely on.
            if (!(await policy.GetAsync(ct)).Oidc)
            {
                ClearChallengeCookies(http);
                await PublishFailureAsync(audit, slug, "method_disabled", ct);
                return Results.Redirect("/?error=provider_unavailable");
            }

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

            // Before the session is issued, so a provider that works is on
            // record as working even if something later in this request fails.
            // #94's guard reads it to decide whether local sign-in may be
            // switched off.
            await store.RecordSuccessfulSignInAsync(result.ProviderId, DateTime.UtcNow, ct);

            await ReconcileAsync(
                reconciler, audit, result.User, result.ProviderId, slug, "oidc", result.Claims, ct);

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

        // ------------------------------------------------------------------
        // SAML 2.0 (#93)
        // ------------------------------------------------------------------

        // The SP metadata document. Its URL is also this service provider's
        // entity ID, which is why the two are built from the same helper: an
        // entity ID that does not resolve to the metadata describing it is a
        // standing source of "the IdP says the audience is wrong" tickets.
        app.MapGet("/api/auth/saml/{slug}/metadata", async (
            string slug, HttpContext http, ISamlSignInService saml, CancellationToken ct) =>
        {
            var xml = await saml.BuildMetadataAsync(slug, AcsUri(http, slug), SpEntityId(http, slug), ct);
            return xml is null
                ? Results.NotFound()
                : Results.Content(xml, "application/samlmetadata+xml");
        }).AllowAnonymous();

        app.MapGet("/api/auth/saml/{slug}/challenge", async (
            string slug,
            string? returnUrl,
            HttpContext http,
            ISamlSignInService saml,
            ISignInMethodPolicy policy,
            CancellationToken ct) =>
        {
            if (!(await policy.GetAsync(ct)).Saml)
            {
                return Results.Redirect("/?error=provider_unavailable");
            }

            var redirect = await saml.BuildAuthnRequestUrlAsync(
                slug, AcsUri(http, slug), SpEntityId(http, slug), SafeReturn(returnUrl), ct);

            // Same silence as the OIDC challenge: "no such provider" and
            // "disabled" look identical to an anonymous caller.
            return redirect is null
                ? Results.Redirect("/?error=provider_unavailable")
                : Results.Redirect(redirect);
        }).AllowAnonymous();

        // The assertion consumer service. This is a cross-site form POST made by
        // the identity provider, so the two ordinary CSRF defenses cannot apply:
        // the auth cookie does not exist yet, and no antiforgery token can reach
        // a form this server never rendered. See the CSRF threat model in
        // Program.cs, case 4 — what stands in for them is that the request body
        // is a signed document, checked against the provider's certificate, with
        // its audience, destination, validity window and one-time use all
        // enforced before anything is trusted. SamlSignInServiceTests asserts
        // each of those rejections individually, because this exemption is only
        // sound while every one of them holds, and
        // AnonymousMutationInventoryTests pins that no second route claims the
        // same argument.
        app.MapPost("/api/auth/saml/{slug}/acs", async (
            string slug,
            HttpContext http,
            ISamlSignInService saml,
            IIdentityProviderStore store,
            IAuditEventPublisher audit,
            IClaimGroupReconciler reconciler,
            ISignInMethodPolicy policy,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("AutoNate.Web.Endpoints.FederatedSignInEndpoints");

            // As with the OIDC callback: an assertion in flight when the method
            // was switched off must not still get somebody in.
            if (!(await policy.GetAsync(ct)).Saml)
            {
                await PublishSamlFailureAsync(audit, slug, "method_disabled", ct);
                return Results.Redirect("/?error=provider_unavailable");
            }

            if (!http.Request.HasFormContentType)
            {
                await PublishSamlFailureAsync(audit, slug, "not_a_form_post", ct);
                return Results.Redirect("/?error=invalid");
            }

            var form = await http.Request.ReadFormAsync(ct);
            var samlResponse = form["SAMLResponse"].ToString();
            if (string.IsNullOrEmpty(samlResponse))
            {
                await PublishSamlFailureAsync(audit, slug, "missing_response", ct);
                return Results.Redirect("/?error=invalid");
            }

            var result = await saml.CompleteAsync(
                slug, samlResponse, AcsUri(http, slug), SpEntityId(http, slug), ct);

            if (!result.Succeeded || result.User is null)
            {
                await PublishSamlFailureAsync(audit, slug, result.Reason.ToString(), ct);
                log.LogWarning(
                    "SAML sign-in through {Slug} refused: {Reason}. {Diagnostic}",
                    slug, result.Reason, result.Diagnostic);
                return Results.Redirect("/?error=invalid");
            }

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

            await store.RecordSuccessfulSignInAsync(result.ProviderId, DateTime.UtcNow, ct);

            await ReconcileAsync(
                reconciler, audit, result.User, result.ProviderId, slug, "saml", result.Attributes, ct);

            var principal = PrincipalFactory.Build(result.User, $"saml:{slug}");
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
                details: new { authSource = $"saml:{slug}", provider = slug, accountCreated = result.AccountCreated },
                ct);

            // RelayState, not a cookie. The challenge cookies work for OIDC
            // because the IdP returns the browser with a top-level GET, which
            // carries a SameSite=Lax cookie; SAML returns it with a cross-site
            // POST, which does not. RelayState is the protocol's own answer, and
            // it goes through the same SafeReturn filter because it round-trips
            // through the browser and is therefore caller-controlled.
            return Results.Redirect(SafeReturn(form["RelayState"].ToString()));
        }).AllowAnonymous().DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// Brings the user's IdP-derived group memberships into line with the claims
    /// this sign-in carried, and audits any change.
    /// </summary>
    /// <remarks>
    /// One helper for both protocols, called from both callbacks. #93's
    /// acceptance criteria ask for claim mapping "without a parallel mapping
    /// surface", and two call sites into one reconciler is what that means in
    /// practice — two reconcilers would eventually disagree about who gets what,
    /// and the lenient one would win.
    ///
    /// It runs on <em>every</em> sign-in, not only the first. A reconciler
    /// invoked once at provisioning would never revoke, so removing someone from
    /// a group at the identity provider would leave their access here intact.
    ///
    /// A failure here does not fail the sign-in. The user authenticated
    /// correctly; refusing them entry because a membership could not be written
    /// would turn a database hiccup into an outage, and the reconciliation is
    /// idempotent — their next sign-in fixes it.
    /// </remarks>
    private static async Task ReconcileAsync(
        IClaimGroupReconciler reconciler,
        IAuditEventPublisher audit,
        LocalUser user,
        Guid providerId,
        string slug,
        string protocol,
        IReadOnlyDictionary<string, string[]> claims,
        CancellationToken ct)
    {
        if (providerId == Guid.Empty) return;

        var result = await reconciler.ReconcileAsync(user.UserId, providerId, claims, ct);
        if (!result.ChangedAnything) return;

        // Only changes are audited. The steady state is somebody signing in with
        // the claims they had yesterday, so auditing every reconciliation would
        // make almost every event a non-event — and a log nobody reads is not a
        // record, whatever it contains.
        foreach (var groupId in result.Added)
        {
            await audit.PublishAsync(
                IamEventTopic.TopicName,
                IdentityEventTypes.ClaimGroupGranted,
                IamResourceKinds.GroupMember,
                resource: new { groupId, userId = user.UserId },
                details: new { source = "idp", provider = slug, protocol },
                ct);
        }

        foreach (var groupId in result.Removed)
        {
            await audit.PublishAsync(
                IamEventTopic.TopicName,
                IdentityEventTypes.ClaimGroupRevoked,
                IamResourceKinds.GroupMember,
                resource: new { groupId, userId = user.UserId },
                details: new { source = "idp", provider = slug, protocol },
                ct);
        }
    }

    private static bool MethodEnabled(SignInMethods methods, string kind) =>
        kind == IdentityProviderKinds.Oidc ? methods.Oidc : methods.Saml;

    private static Task PublishSamlFailureAsync(
        IAuditEventPublisher audit, string slug, string reason, CancellationToken ct) =>
        audit.PublishAsync(
            AuthEventTopic.TopicName,
            AuthEventTypes.LoginFailed,
            AuthEventTopic.ResourceKind,
            resource: new { provider = slug },
            details: new { authSource = $"saml:{slug}", provider = slug, reason },
            ct);

    /// <summary>Where the IdP posts the assertion.</summary>
    private static string AcsUri(HttpContext http, string slug) =>
        $"{http.Request.Scheme}://{http.Request.Host}/api/auth/saml/{Uri.EscapeDataString(slug)}/acs";

    /// <summary>
    /// This service provider's entity ID, and the audience an assertion must name.
    /// </summary>
    /// <remarks>
    /// The metadata URL doubles as the entity ID — a dereferenceable identifier,
    /// which is what SAML recommends and what most IdP admin screens expect to
    /// be handed. It is per-provider rather than per-host so that two IdPs
    /// configured against the same Auton8 cannot be handed an assertion meant
    /// for the other.
    /// </remarks>
    private static string SpEntityId(HttpContext http, string slug) =>
        $"{http.Request.Scheme}://{http.Request.Host}/api/auth/saml/{Uri.EscapeDataString(slug)}/metadata";

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
