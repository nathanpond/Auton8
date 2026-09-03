using System.Net;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The liveness probe is anonymous; the other two health endpoints are not.
/// </summary>
/// <remarks>
/// This is the story's regression risk. A container healthcheck needs an
/// endpoint it can reach without credentials, and the tempting shortcut is to
/// drop the authorization on the whole health group — which would publish
/// component status, internal topology and exception messages to anything that
/// can reach the port.
/// </remarks>
public sealed class HealthLivenessEndpointTests
{
    // The factory enables DevelopmentAutoLogin, which signs every client in as
    // admin. Every assertion in this class is about what an *unauthenticated*
    // caller sees, so it has to be off — with it on, the liveness test passes
    // whether or not the endpoint is anonymous, and proves nothing.
    private static Task<AutoNateWebApplicationFactory> CreateAnonymousAsync() =>
        AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?> { ["DevelopmentAutoLogin:Enabled"] = "false" });

    [Fact]
    public async Task Liveness_answers_an_unauthenticated_caller()
    {
        await using var factory = await CreateAnonymousAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_reveals_nothing_beyond_being_alive()
    {
        await using var factory = await CreateAnonymousAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health/live");
        var body = await response.Content.ReadAsStringAsync();

        // Anything here is readable by anything that can reach the port. The
        // specific leaks worth naming: a version tells an attacker which
        // advisories apply, and component or configuration detail maps the
        // deployment.
        Assert.DoesNotContain("version", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Flowable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dapr", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(body.Length < 64, $"Liveness body should be trivial; got {body.Length} chars.");
    }

    [Fact]
    public async Task The_informative_health_endpoints_stay_authenticated()
    {
        // The regression guard. If a future change makes these anonymous to
        // "fix" a probe, this fails.
        await using var factory = await CreateAnonymousAsync();
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/api/health/system", "/api/health/dapr" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect
                    or HttpStatusCode.Found,
                $"{path} answered {(int)response.StatusCode} to an unauthenticated caller; "
                + "it exposes component status and internal topology and must stay gated.");
        }
    }
}
