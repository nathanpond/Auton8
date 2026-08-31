using System.Net;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Regression guard for #132: the SPA fallback route must serve index.html at
/// the site root as well as for deep links, and must never swallow /api.
///
/// Program.cs only wires the static-file / fallback pipeline when
/// WebRootPath exists, so these tests boot the host against a throw-away
/// wwwroot containing a minimal SPA shell.
/// </summary>
public sealed class SpaRootFallbackTests : IAsyncLifetime
{
    private string _webRoot = string.Empty;
    private AutoNateWebApplicationFactory? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "autonate-spa-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "index.html"),
            "<!doctype html><html><body><div id=\"root\"></div></body></html>");
        _factory = await AutoNateWebApplicationFactory.CreateAsync(webRoot: _webRoot);
        _client = _factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        try { Directory.Delete(_webRoot, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/home")]
    [InlineData("/records/some-type/00000000-0000-0000-0000-000000000000")]
    public async Task SpaRoutes_ServeTheShell(string path)
    {
        // "/" is the case that regressed: RegexRouteConstraint rejects a
        // missing catch-all value, so a constrained "{*path:...}" fallback
        // alone never matches the root URL.
        var response = await _client!.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"root\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/definitely-not-a-route")]
    [InlineData("/api")]
    public async Task UnknownApiPaths_Are404_NotTheShell(string path)
    {
        var response = await _client!.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("id=\"root\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticFile_IsServedDirectly()
    {
        var response = await _client!.GetAsync("/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
