using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Smoke coverage for the workflow-execution override surface. The override
/// authorization gate itself is exhaustively tested in
/// AutoNate.Web.Tests/Authorization/WorkflowOverrideEnforcementTests.cs (HTTP
/// 403/204 by grant); this file just verifies the SPA renders the new pages
/// and doesn't crash on the override-aware refactor.
/// </summary>
public sealed class WorkflowOverrideTests : IClassFixture<AutoNateE2EFixture>
{
    private readonly AutoNateE2EFixture _fixture;

    public WorkflowOverrideTests(AutoNateE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WorkflowExecutionsPage_RendersForSeededAdmin()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await SignInAsAdminAsync(page);
        await page.GotoAsync("/workflow-executions");

        // The page header is the cheapest assertion that the route resolved
        // and the React tree mounted. SuperAdmin always has override, but
        // there may be no executions to drill into in a fresh test database,
        // so we don't open the modal here — just confirm the page renders.
        await page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Executions" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task ExecutionDeepLink_RendersWithMissingId_ShowsClearError()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await SignInAsAdminAsync(page);

        // /executions/:id with an obviously fake id: the route should resolve
        // (proving the new route registration works) and ExecutionContent
        // should render with a Flowable-not-found error rather than a blank
        // page or a JS exception.
        await page.GotoAsync("/executions/this-instance-does-not-exist");

        // The page-level error banner is the alert role; we just want it to
        // appear within the timeout, regardless of exact wording.
        await page.GetByRole(AriaRole.Alert)
            .First.WaitForAsync(new() { Timeout = 15_000 });
    }

    private static async Task SignInAsAdminAsync(IPage page)
    {
        await page.GotoAsync("/");
        await page.Locator("#username").FillAsync("admin");
        await page.Locator("#password").FillAsync("admin");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign me in" }).ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/home"));
    }
}
