using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AuthEndpointsTests
{
    [Fact]
    public async Task GetMe_AfterAutoLogin_ReturnsAuthenticatedAdmin()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Hitting any GET kicks off the dev auto-login middleware before /api/auth/me runs.
        var me = await client.GetFromJsonAsync<AuthMeDto>("/api/auth/me");

        Assert.NotNull(me);
        Assert.True(me.Authenticated);
        Assert.Equal("admin", me.Username);
    }

    [Fact]
    public async Task PostLogout_ReturnsOk()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Prime auth so the cookie exists, then sign out.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record AuthMeDto(bool Authenticated, string? UserId, string? Username);
}
