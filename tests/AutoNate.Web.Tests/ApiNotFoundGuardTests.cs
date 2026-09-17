using System.Net;
using AutoNate.Web.Tests.Infrastructure;
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
    /// <summary>
    /// The guard is wired OUTSIDE the wwwroot conditional, structurally (#514).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The behavioural test below cannot catch this regression anywhere it
    /// actually runs. `ci.yml` downloads the SPA bundle into
    /// `src/AutoNate.Web/wwwroot` before the backend suite, and a developer's
    /// main checkout has a built `wwwroot/` too — so with the directory present,
    /// re-nesting the middleware inside
    /// <c>if (Directory.Exists(app.Environment.WebRootPath))</c> leaves it green
    /// on GitHub and locally alike. It only went red in a checkout that had
    /// never built the SPA, which is how #388 was found and is not a state any
    /// gate reproduces on purpose.
    /// </para>
    /// <para>
    /// So this asserts the source ordering instead, which holds regardless of
    /// whether wwwroot exists. The guard is a statement about API routing, not
    /// about static files: an unknown /api path must answer a clean,
    /// uncacheable 404 whether or not the SPA has been built.
    /// </para>
    /// <para>
    /// What this does NOT cover: that the middleware still runs after routing.
    /// Nothing calls UseRouting explicitly, so WebApplication inserts it ahead
    /// of the first user middleware; the behavioural test below is what proves
    /// <c>GetEndpoint()</c> is populated.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_api_404_guard_is_not_nested_inside_the_wwwroot_conditional()
    {
        var program = File.ReadAllLines(
            Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", "Program.cs"));

        var guard = Array.FindIndex(program, line =>
            line.Contains("http.Request.Path.StartsWithSegments(\"/api\")", StringComparison.Ordinal)
            && line.Contains("GetEndpoint() is null", StringComparison.Ordinal));
        Assert.True(guard >= 0, "the /api 404 guard is gone from Program.cs.");

        var conditional = Array.FindIndex(program, line =>
            line.StartsWith("if (Directory.Exists(app.Environment.WebRootPath))", StringComparison.Ordinal));
        Assert.True(
            conditional >= 0,
            "the wwwroot conditional is gone from Program.cs; this guard's premise needs rechecking.");

        Assert.True(
            guard < conditional,
            $"the /api 404 guard is at line {guard + 1}, inside or after the wwwroot conditional "
            + $"at line {conditional + 1}. Nested there it disappears in any checkout without "
            + "src/AutoNate.Spa/dist — every git worktree — and no suite that runs with a built "
            + "wwwroot can tell (#388, #514).");

        // And it must set no-store where it answers, not merely exist: the
        // header is the half that made the original failure legible.
        var body = string.Join("\n", program[guard..Math.Min(guard + 8, program.Length)]);
        Assert.Contains("no-store", body, StringComparison.Ordinal);
        Assert.Contains("Status404NotFound", body, StringComparison.Ordinal);
    }

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
