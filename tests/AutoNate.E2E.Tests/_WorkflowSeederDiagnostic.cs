using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.E2E.Tests;

// Temporary diagnostic for Phase 4 — verifies the workflow seeder path
// reaches Flowable and whether the started instance shows up in
// /api/executions. Delete after the suite is stable.
public sealed class _WorkflowSeederDiagnostic : E2ETestBase
{
    private readonly ITestOutputHelper _output;

    public _WorkflowSeederDiagnostic(AutoNateE2EFixture fixture, ITestOutputHelper output)
        : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task Dump_PostStartState()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf");
        var instanceName = TestNames.Prefixed("diag");

        _output.WriteLine($"processKey = {processKey}");
        _output.WriteLine($"instanceName = {instanceName}");

        var wf = await seeder.CreateAndPublishWorkflowAsync(processKey, workflowName);
        _output.WriteLine($"workflow created+published: id={wf.Id}");

        var exec = await seeder.StartExecutionAsync(processKey, instanceName);
        _output.WriteLine($"execution started: id={exec.Id}");

        // Inspect /api/workflows/ to confirm save persisted.
        var workflowsResponse = await page.APIRequest.GetAsync("/api/workflows/");
        var workflowsBody = await workflowsResponse.TextAsync();
        _output.WriteLine($"/api/workflows/ status={workflowsResponse.Status}");
        _output.WriteLine($"  body (truncated): {Truncate(workflowsBody, 1500)}");

        // Inspect /api/executions/ to see what Flowable + visibility filter
        // hand back.
        var executionsResponse = await page.APIRequest.GetAsync("/api/executions/");
        var executionsBody = await executionsResponse.TextAsync();
        _output.WriteLine($"/api/executions/ status={executionsResponse.Status}");
        _output.WriteLine($"  body (truncated): {Truncate(executionsBody, 2500)}");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
