using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

// Needs the Flowable engine (infra/docker-compose.yml `flowable`), which slim
// does not stand up — publishing a workflow there fails with
// "Connection refused". Traited so the tier boundary is a capability rather
// than a hand-maintained list of class names that would silently rot.
[Collection(AutoNateE2ECollection.Name)]
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

    /// <summary>
    /// E2E-036: each completion mode produces a different task surface (#78).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than in <c>WorkflowStudioEditorTests</c>, because this half
    /// genuinely needs the engine</b> — it publishes, starts an instance and opens
    /// the resulting task. The other half of E2E-036, that the studio's panel
    /// writes the mode and it survives a reload, is untraited and lives there;
    /// splitting them is the story's own instruction about tiers, not a
    /// convenience.
    /// </para>
    /// <para>
    /// <b>The modes are seeded rather than clicked</b>, because driving the panel
    /// three times, publishing three times and starting three instances would
    /// prove the panel three times and each surface once — and the panel is
    /// already proven once, precisely. What is unproven is what the three modes
    /// DO, which is what this asserts.
    /// </para>
    /// <para>
    /// <b>The complement is the whole test.</b> All three modes open something, so
    /// "a surface appeared" cannot tell them apart: the built-in dialog and a form
    /// modal are both dialogs, and asserting one of them would pass for the other.
    /// Each row therefore asserts what the OTHER modes would not do.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Each_user_task_completion_mode_opens_its_own_surface()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var me = await page.APIRequest.GetAsync("/api/auth/me");
        Assert.True(me.Ok, await me.TextAsync());
        using var meJson = System.Text.Json.JsonDocument.Parse(await me.TextAsync());
        var adminUserId = meJson.RootElement.GetProperty("userId").GetString()!;

        var form = await seeder.CreateFormAsync(
            name: TestNames.Prefixed("task-form"),
            shortCode: $"e2e-{TestNames.ShortSlug()}",
            siteAvailable: true);
        await seeder.PublishFormAsync(form.Id);

        // ---- simple: Auton8's own completion modal --------------------------
        var simpleKey = $"e2e_{TestNames.ShortSlug()}";
        var simpleName = TestNames.Prefixed("mode-simple");
        await seeder.CreateAndPublishWorkflowAsync(simpleKey, simpleName, assignee: adminUserId);
        await seeder.StartExecutionAsync(simpleKey, simpleName);

        await OpenMyTaskAsync(page, simpleName);
        var builtIn = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(builtIn).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The built-in surface's defining feature: Auton8's own completion
        // button, which a form mode REPLACES rather than decorates.
        await Assertions.Expect(
            builtIn.GetByRole(AriaRole.Button, new() { Name = "Complete Task" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ---- modal form: a dialog that is NOT the built-in one ---------------
        var modalKey = $"e2e_{TestNames.ShortSlug()}";
        var modalName = TestNames.Prefixed("mode-modal");
        await seeder.CreateAndPublishWorkflowAsync(
            modalKey, modalName, assignee: adminUserId,
            userFormMode: "modal", userFormShortCode: form.ShortCode);
        await seeder.StartExecutionAsync(modalKey, modalName);

        await OpenMyTaskAsync(page, modalName);
        var formModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(formModal).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // THE COMPLEMENT, and the only assertion that separates this from the row
        // above: both open a dialog, so "a dialog appeared" is true of either. The
        // form mode replaces Auton8's completion button with the author's form.
        await Assertions.Expect(
            formModal.GetByRole(AriaRole.Button, new() { Name = "Complete Task" }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ---- page form: no dialog at all, a route ---------------------------
        var pageKey = $"e2e_{TestNames.ShortSlug()}";
        var pageName = TestNames.Prefixed("mode-page");
        await seeder.CreateAndPublishWorkflowAsync(
            pageKey, pageName, assignee: adminUserId,
            userFormMode: "page", userFormShortCode: form.ShortCode);
        await seeder.StartExecutionAsync(pageKey, pageName);

        await OpenMyTaskAsync(page, pageName);

        // Navigates instead of opening anything. Asserted on the URL, because
        // that is what "page mode" MEANS and it is the one surface a dialog
        // assertion could never be satisfied by.
        await page.WaitForURLAsync(u => u.Contains("/workflow-tasks/", StringComparison.Ordinal)
            && u.Contains("/form", StringComparison.Ordinal),
            new() { Timeout = 20_000 });
    }

    private static async Task OpenMyTaskAsync(IPage page, string workflowName)
    {
        await page.GotoAsync("/home");

        // Row-scoped. The same name renders in the recent-executions list too, and
        // Flowable state is shared across the dev database, so several "Open"
        // buttons sit beside each other.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = workflowName });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Open", Exact = true }).ClickAsync();
    }
}
