using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.Web.Tests.Authorization;

/// <summary>
/// Pins the complete set of endpoints that mutate state, allow anonymous
/// callers, and validate no antiforgery token.
/// </summary>
/// <remarks>
/// The CSRF threat model in Program.cs says that an anonymous mutating endpoint
/// must validate an antiforgery token or a server-to-server shared secret,
/// "never both off" — with one documented exception, the SAML assertion
/// consumer (#93), which validates a signed assertion instead.
///
/// That rule is honour-system unless something counts. This counts. A new
/// endpoint that lands in this set fails the test, and the fix is either to add
/// the missing defence or to add a line here saying what defends it — which is a
/// deliberate, reviewable act rather than a quiet omission.
///
/// This is not <c>AuthorizationGatePresenceTests</c>'s job: that one asks
/// whether an authorization decision was *made*, and <c>AllowAnonymous</c> is a
/// perfectly good answer to it. This asks what protects the ones that answered
/// "anonymous".
/// </remarks>
[Trait("Category", "Integration")]
public sealed class AnonymousMutationInventoryTests(ITestOutputHelper output)
{
    /// <summary>Route pattern → what defends it instead of an antiforgery token.</summary>
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["/api/auth/check"] =
            "Reads session state and mutates nothing; POST only so a session probe is not cached.",
        ["/api/auth/logout"] =
            "Ends a session. Forging it costs a user their session and gains an attacker nothing.",
        ["/api/auth/saml/{slug}/acs"] =
            "The SAML assertion consumer (#93). Receives an unsolicited cross-site POST from the "
            + "identity provider by design, so neither a cookie nor a token can accompany it. The "
            + "body is a signed assertion, refused unless the signature validates against the "
            + "provider's certificate and the audience, destination, validity window and one-time "
            + "use all check out — see SamlSignInServiceTests.",
        ["/api/workflow-behaviors/{key}/execute"] =
            "Server-to-server callback from Flowable, gated by SharedSecretEndpointFilter.",
        ["/internal/yjs-auth"] =
            "Server-to-server callback from the Hocuspocus sidecar, gated by "
            + "YjsInternalSecretEndpointFilter.",
        ["/internal/yjs-webhook"] =
            "Server-to-server callback from the Hocuspocus sidecar, gated by "
            + "YjsInternalSecretEndpointFilter.",
    };

    [Fact]
    public async Task Every_anonymous_mutating_endpoint_is_accounted_for()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var found = new List<string>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints)
        {
            if (endpoint is not RouteEndpoint route) continue;

            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!methods.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE")) continue;
            if (route.Metadata.GetMetadata<IAllowAnonymous>() is null) continue;

            // No metadata at all means the antiforgery middleware skips the
            // endpoint, so absence and an explicit DisableAntiforgery() are the
            // same posture and both belong in this inventory.
            if (route.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation == true) continue;

            found.Add(route.RoutePattern.RawText ?? "(unknown)");
        }

        var unexpected = found.Where(p => !Expected.ContainsKey(p)).Distinct().Order().ToList();
        var vanished = Expected.Keys.Where(p => !found.Contains(p)).Order().ToList();

        foreach (var p in unexpected)
        {
            output.WriteLine(
                $"{p} mutates state, allows anonymous callers, and validates no antiforgery token. "
                + "Add an antiforgery token, a shared-secret endpoint filter, or — if the request "
                + "body is itself a verified signed credential — a line in Expected saying so.");
        }

        foreach (var p in vanished)
        {
            output.WriteLine($"{p} is listed here but no longer matches. Remove the stale entry.");
        }

        Assert.Empty(unexpected);
        Assert.Empty(vanished);
    }

    [Fact]
    public async Task The_saml_assertion_consumer_is_the_only_route_exempt_on_signature_grounds()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        // Every other entry above is defended by a shared secret or is harmless.
        // The "a signed body stands in for the token" argument justifies exactly
        // one route, and copying it to a second without the signature checks
        // would be the way this exemption turns into a hole.
        var signatureBacked = Expected
            .Where(e => e.Value.Contains("signed assertion", StringComparison.Ordinal))
            .Select(e => e.Key)
            .ToList();

        Assert.Equal(["/api/auth/saml/{slug}/acs"], signatureBacked);
    }
}
