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
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowOverrideTests
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

        await AutoNateE2EFixture.SignInAsAdminAsync(page);
        await page.GotoAsync("/workflow-executions");

        await Assertions
            .Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Executions" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The four status-count cards (RUNNING / COMPLETED / CANCELLED /
        // ERRORED) only render once useExecutions() resolves. Asserting on
        // them proves the executions API actually responded, not just that
        // the route registered. A fresh DB has zero executions, so we don't
        // care about the numeric value — only that the cards mount.
        await Assertions.Expect(page.GetByText("RUNNING")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("COMPLETED")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("CANCELLED")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("ERRORED")).ToBeVisibleAsync();

        // And no error banner — if useExecutions() threw we'd see a red Alert
        // with role="alert". The flash slot uses role="status" for success,
        // so this only catches the failure case.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ExecutionDeepLink_RendersWithMissingId_ShowsClearError()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await AutoNateE2EFixture.SignInAsAdminAsync(page);

        // /executions/:id with a fake id: the route should resolve (proving
        // the route registration works) and ExecutionContent should render a
        // describeError alert rather than a blank page or a JS exception.
        await page.GotoAsync("/executions/this-instance-does-not-exist");

        // PageHeader "Execution" is the cheapest proof the route mounted. We
        // need Exact=true because ExecutionContent also renders a sub-heading
        // like "Execution this-instance-does-not-exist" once the API errors,
        // which would otherwise match the same Name filter.
        await Assertions
            .Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Execution", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The Flowable lookup fails -> describeError populates a red Alert.
        await Assertions
            .Expect(page.GetByRole(AriaRole.Alert).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
