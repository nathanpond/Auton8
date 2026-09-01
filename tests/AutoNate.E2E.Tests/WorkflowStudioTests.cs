using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

// Needs the Flowable engine (infra/docker-compose.yml `flowable`), which the
// CI E2E job does not host — publishing a workflow there fails with
// "Connection refused". Traited so CI can exclude it by capability rather
// than by a hand-maintained list of class names that would silently rot.
[Trait("RequiresService", "Flowable")]
public sealed class WorkflowStudioTests : E2ETestBase
{
    public WorkflowStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task WorkflowStudio_CreatesSavesPublishesPausesResumesAndStartsModel()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var workflowName = TestNames.Prefixed("studio-workflow");

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Create workflow model", Exact = true })
            .ClickAsync(new() { Force = true });
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Create Workflow Model" });
        await dialog.GetByLabel("Workflow Name").FillAsync(workflowName);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText($"Created workflow model '{workflowName}'."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText($"Saved workflow model '{workflowName}'."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText($"Published '{workflowName}'", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText($"Paused '{workflowName}'.", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }))
            .ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText($"Resumed '{workflowName}'.", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Start Instance" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Started process instance", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
