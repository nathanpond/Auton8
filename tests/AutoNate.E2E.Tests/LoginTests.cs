using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class LoginTests : IClassFixture<AutoNateE2EFixture>
{
    private readonly AutoNateE2EFixture _fixture;

    public LoginTests(AutoNateE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeededAdmin_CanSignIn_AndLandsOnHome()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");

        await page.Locator("#username").FillAsync("admin");
        await page.Locator("#password").FillAsync("admin");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign me in" }).ClickAsync();

        // Server redirects to /home (or /spa/home if the SPA supplied that returnUrl).
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/home"));

        // Confirm the session cookie was actually set by hitting the auth endpoint.
        var response = await page.APIRequest.GetAsync("/api/auth/me");
        Assert.True(response.Ok);
        var json = await response.JsonAsync();
        Assert.True(json!.Value.GetProperty("authenticated").GetBoolean());
        Assert.Equal("admin", json.Value.GetProperty("username").GetString());
    }
}
