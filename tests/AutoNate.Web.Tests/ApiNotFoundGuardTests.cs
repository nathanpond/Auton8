using System.Net;
using Xunit;

namespace AutoNate.Web.Tests;

// Pins the /api 404 guard in Program.cs. Unknown /api paths must produce a
// clean, uncacheable 404 rather than falling through to the SPA index.html
// catch-all — and the guard must NOT be a route endpoint, or it competes
// with real endpoints during content-type negotiation (see
// SystemIssueEndpointsTests.Resolve_with_no_body_still_works, which is the
// regression that surfaced when it was a MapFallback route).
[Trait("Category", "Integration")]
public sealed class ApiNotFoundGuardTests
{
    [Fact]
    public async Task Unknown_api_path_returns_404_not_spa_index()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var resp = await client.GetAsync("/api/definitely-not-a-registered-route/123");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("no-store", resp.Headers.CacheControl?.ToString());
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_api_path_is_404_for_anonymous_callers_not_a_login_redirect()
    {
        // The guard runs after routing but before any endpoint executes, so
        // there is no endpoint whose auth metadata could turn this into a
        // 401 / redirect-to-login. A 404 here is the correct, non-leaking
        // answer for "this route doesn't exist".
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.GetAsync("/api/definitely-not-a-registered-route/123");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Bodyless_post_to_a_json_endpoint_still_reaches_its_handler()
    {
        // The regression that motivated the middleware: a MapFallback route
        // under /api competed in content-type negotiation and swallowed
        // body-less POSTs to real endpoints with 404. /api/auth/logout takes
        // no body and is the cheapest such endpoint to probe.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var resp = await client.PostAsync("/api/auth/logout", content: null);

        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
