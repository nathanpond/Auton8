using System.Text.RegularExpressions;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Phase 4 of the comprehensive E2E plan
/// (<c>docs/plans/2026-05-29-playwright-e2e-coverage.md</c>) — workflows and
/// executions. Per the plan's smoke+API-backed decision, the BPMN
/// <c>WorkflowStudio</c> is asserted only at the page-mount level; the
/// interesting behavior — start, list, detail, cancel, delete — is driven by
/// seeding workflows + instances through the real API (which reaches Flowable
/// running in the dev docker stack) and asserting against the SPA's
/// <c>/workflow-executions</c> UI.
///
/// Two implementation realities each test code has to dance around:
///
/// 1. <c>WorkflowExecutionEventListener</c> in the Flowable Java extension
///    auto-renames every started instance to <c>"{ModelName} - {timestamp}"</c>,
///    overriding the name passed via the .NET start endpoint. So tests locate
///    rows / cancel buttons by the unique <c>workflowName</c> they minted
///    (which is the prefix of the auto-generated display name), not the
///    instance name they sent.
///
/// 2. <c>DataTable.tsx</c> accepts <c>getRowAriaLabel</c> for API parity but
///    doesn't propagate it to mantine-datatable rows (lines 515-517). So
///    rows have no accessible names; we locate cells by their text content
///    instead of <c>role=row</c>.
///
/// Flowable's state is shared with the dev environment (its own Postgres
/// schema, not our ephemeral <c>AutoNate_E2E</c> DB). Each test mints a
/// unique <c>processKey</c> + workflow name so concurrent or repeated runs
/// don't collide with each other or with developer workflows. We deliberately
/// do NOT exercise <c>POST /api/executions/delete-all</c> — that endpoint
/// wipes every execution in Flowable, including dev work in progress; per-row
/// delete is the safe equivalent.
///
/// The existing <c>WorkflowOverrideTests</c> already covers the empty-list
/// status-stat-card render and the deep-link error path; those aren't
/// duplicated here.
/// </summary>
// Needs the Flowable engine (infra/docker-compose.yml `flowable`), which the
// CI E2E job does not host — publishing a workflow there fails with
// "Connection refused". Traited so CI can exclude it by capability rather
// than by a hand-maintained list of class names that would silently rot.
[Trait("RequiresService", "Flowable")]
public sealed class WorkflowExecutionTests : E2ETestBase
{
    public WorkflowExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task WorkflowStudio_PageMountsWithoutError()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/workflow");

        // Title order={1} at line 1053 of WorkflowStudio.tsx.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // No red Alert means useWorkflows() / useExecutions() / Bpmn modeler
        // mount didn't blow up. Mantine's success/status alerts use role=status,
        // so role=alert is unique to the error case.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task StartedExecution_AppearsInList_WithRunningStatus()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf");
        await seeder.CreateAndPublishWorkflowAsync(processKey, workflowName);
        await seeder.StartExecutionAsync(processKey, instanceName: TestNames.Prefixed("run"));

        await page.GotoAsync("/workflow-executions");

        // The Flowable extension renames each started instance to
        // "{workflowName} - {timestamp}", so the displayName cell always
        // starts with workflowName. Asserting `GetByText(workflowName)` finds
        // the row by its unique-per-test prefix.
        await Assertions.Expect(page.GetByText(workflowName).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task StartedExecution_RowClick_OpensExecutionDetailModal()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf");
        await seeder.CreateAndPublishWorkflowAsync(processKey, workflowName);
        await seeder.StartExecutionAsync(processKey, instanceName: TestNames.Prefixed("click"));

        await page.GotoAsync("/workflow-executions");

        var nameCell = page.GetByText(workflowName).First;
        await Assertions.Expect(nameCell).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // mantine-datatable's onRowClick fires on any cell click, so clicking
        // the displayName cell triggers the row-open path that mounts the
        // execution-detail modal (WorkflowExecutions sets selectedId state).
        await nameCell.ClickAsync();

        // The modal renders ExecutionContent, which uses its own
        // `<Title order={3}>{detail.name}</Title>` (not the h1 PageHeader
        // "Execution" that the deep-link /executions/:id route uses). The
        // most reliable modal-specific signal is its Close button, paired
        // with the Diagram tab that ExecutionContent always renders.
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Close" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(dialog.GetByRole(AriaRole.Tab, new() { Name = "Diagram" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task CancelExecution_FromList_ConfirmsAndFlipsStatusToCancelled()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf");
        await seeder.CreateAndPublishWorkflowAsync(processKey, workflowName);
        await seeder.StartExecutionAsync(processKey, instanceName: TestNames.Prefixed("cancel"));

        await page.GotoAsync("/workflow-executions");

        // Wait for the row to land first so the cancel ActionIcon (which
        // carries the displayName-suffixed aria-label) has actually rendered.
        await Assertions.Expect(page.GetByText(workflowName).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The cancel button's aria-label is "Cancel execution {displayName}",
        // and displayName starts with workflowName per the auto-rename above.
        // A regex anchored on the prefix matches without us having to know
        // the auto-generated timestamp suffix.
        await page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new Regex($"^Cancel execution {Regex.Escape(workflowName)}")
        }).ClickAsync();

        // ConfirmModal title "Cancel execution?" with destructive primary
        // button labeled "Cancel execution" (line 506 of WorkflowExecutions.tsx);
        // the dismiss button is the plain "Cancel".
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel execution", Exact = true }).ClickAsync();

        // After Flowable processes the cancellation and the list refetches,
        // the status cell renders "Cancelled" (the executions stat card at
        // the top of the page uses uppercase "CANCELLED", so Exact=true keeps
        // them distinct).
        await Assertions.Expect(page.GetByText("Cancelled", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task DeleteExecution_FromList_RemovesItFromTheTable()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf");
        await seeder.CreateAndPublishWorkflowAsync(processKey, workflowName);
        await seeder.StartExecutionAsync(processKey, instanceName: TestNames.Prefixed("del"));

        await page.GotoAsync("/workflow-executions");

        var nameCell = page.GetByText(workflowName).First;
        await Assertions.Expect(nameCell).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new Regex($"^Delete execution {Regex.Escape(workflowName)}")
        }).ClickAsync();

        // ConfirmModal title "Delete execution?" with destructive primary
        // button "Delete" (line 506).
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();

        // After Flowable processes the delete and the list refetches the
        // workflowName cell is gone.
        await Assertions.Expect(nameCell).Not.ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task AssignedWorkflowTask_CompleteFromMyTasks_RemovesItFromTheTable()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var meResponse = await page.APIRequest.GetAsync("/api/auth/me");
        var me = await meResponse.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/auth/me.");
        var adminUserId = me.GetProperty("userId").GetString()
            ?? throw new InvalidOperationException("/api/auth/me did not return userId.");

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf-task");
        await seeder.CreateAndPublishWorkflowAsync(processKey, workflowName, assignee: adminUserId);
        await seeder.StartExecutionAsync(processKey, instanceName: TestNames.Prefixed("complete"));

        await page.GotoAsync("/home");
        var taskName = page.GetByText(workflowName).First;
        await Assertions.Expect(taskName).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Flowable state is shared across the dev DB so stale workflow
        // executions from previous runs leave their own "Open" buttons
        // in the My Tasks table. Scope the click to the row containing
        // this test's unique workflowName so we don't trip strict-mode
        // when 5+ "Open" buttons sit beside each other.
        var taskRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = workflowName });
        await taskRow.GetByRole(AriaRole.Button, new() { Name = "Open", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Review" });
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Complete Task" }).ClickAsync();

        await Assertions.Expect(dialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(taskName).Not.ToBeVisibleAsync(new() { Timeout = 20_000 });
    }
}
