using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

[Collection(AutoNateE2ECollection.Name)]
public sealed class LoginTests : E2ETestBase
{
    public LoginTests(AutoNateE2EFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task SeededAdmin_CanSignIn_AndLandsOnHome()
    {
        await using var session = await NewAnonymousSessionAsync();
        var page = session.Page;

        await AutoNateE2EFixture.SignInAsAdminAsync(page);

        // /home should resolve and the Home dashboard heading should render —
        // proves the SPA is mounted with an authenticated session, not just
        // that the server set a cookie.
        Assert.Matches("/home", page.Url);
        await page.GetByRole(AriaRole.Heading, new() { Name = "Home", Exact = true, Level = 1 })
            .WaitForAsync(new() { Timeout = 15_000 });

        // Belt-and-braces: confirm the cookie is actually accepted by the API.
        var response = await page.APIRequest.GetAsync("/api/auth/me");
        Assert.True(response.Ok);
        var json = await response.JsonAsync();
        Assert.True(json!.Value.GetProperty("authenticated").GetBoolean());
        Assert.Equal("admin", json.Value.GetProperty("username").GetString());
    }

    [Fact]
    public async Task BadPassword_RedirectsToLogin_WithInlineError()
    {
        await using var session = await NewAnonymousSessionAsync();
        var page = session.Page;

        await AutoNateE2EFixture.SignInAsync(page, "admin", "definitely-not-the-password");

        // Failed credentials bounce back to / with ?error=invalid; the login
        // page surfaces a Mantine Alert reading "Invalid username or password."
        Assert.Contains("error=invalid", page.Url);
        await page.GetByText("Invalid username or password.")
            .WaitForAsync(new() { Timeout = 5_000 });

        // And the session must NOT be authenticated — guards against a
        // regression where the server sets a cookie before validating creds.
        var response = await page.APIRequest.GetAsync("/api/auth/me");
        var json = await response.JsonAsync();
        Assert.False(json!.Value.GetProperty("authenticated").GetBoolean());
    }
}
