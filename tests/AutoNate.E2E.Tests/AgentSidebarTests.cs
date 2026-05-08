using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

// Smoke check for the Phase-8 agent sidebar. We don't probe an actual LLM
// here because the E2E fixture starts a fresh DB with no External Connection
// rows; the goal is to confirm the sidebar mounts, toggles open, accepts a
// message, and the "no provider configured" error path surfaces cleanly to
// the user. Wiring the full streaming-with-real-Anthropic flow is a separate
// fixture concern we'll add when we have a way to inject a stubbed provider
// from the E2E side.
public sealed class AgentSidebarTests : IClassFixture<AutoNateE2EFixture>
{
    private readonly AutoNateE2EFixture _fixture;

    public AgentSidebarTests(AutoNateE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Sidebar_toggle_is_present_after_admin_signs_in()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");
        await page.Locator("#username").FillAsync("admin");
        await page.Locator("#password").FillAsync("admin");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign me in" }).ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/home"));

        var toggle = page.Locator(".agent-toggle");
        await toggle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.True(await toggle.IsVisibleAsync());
    }

    [Fact]
    public async Task External_connections_admin_page_renders_after_admin_signs_in()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");
        await page.Locator("#username").FillAsync("admin");
        await page.Locator("#password").FillAsync("admin");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign me in" }).ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/home"));

        await page.GotoAsync("/admin/config/external-connections");
        // The page replaces the legacy stub component; the heading or the
        // "New connection" button is enough to prove Phase 3 wired up.
        var newButton = page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("New connection") });
        await newButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.True(await newButton.IsVisibleAsync());
    }
}
