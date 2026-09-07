using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Smoke + create-modal + editor-mounts coverage for the Phase 5 + Phase 6
/// pages of the Data Stores &amp; Analytics Pipeline plan
/// (<c>docs/plans/2026-05-30-data-stores-implementation.md</c>):
/// <c>/pipelines</c>, the lazy-loaded React Flow editor at
/// <c>/pipelines/{id}</c>, and <c>/code-transformers</c>.
/// Pipelines also gets a "create then click into the editor" test that
/// proves the lazy-loaded React Flow chunk mounts — the editor route was
/// one of the files whose strict-mode TS errors fell out of the broken
/// `DataTableColumn` shape in Phase 5, so a render assertion here would
/// have caught that earlier.
/// </summary>
public sealed class PipelinesAdminTests : E2ETestBase
{
    public PipelinesAdminTests(AutoNateE2EFixture fixture) : base(fixture) { }

    // ---- Pipelines ------------------------------------------------------

    [Fact]
    public async Task Pipelines_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/pipelines");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Analytics Pipelines", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pipelines_CreateModal_OpensWithNameField()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/pipelines");

        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(modal.GetByLabel("Name")).ToBeVisibleAsync();
        await Assertions.Expect(
            modal.GetByRole(AriaRole.Button, new() { Name = "Create" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pipelines_CreateAndOpenEditor_MountsReactFlow()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/pipelines");

        var name = TestNames.Prefixed("pipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(name);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // The list row links into the editor; clicking the name navigates
        // to /pipelines/{id}.
        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByText(name).First.ClickAsync();
        await page.WaitForURLAsync("**/pipelines/*", new() { Timeout = 15_000 });

        // PipelineEditor.tsx — the toolbar carries the pipeline name as
        // an h2, and the left palette has the four node-kind buttons.
        // The "+ Dataset source" button is the cheapest unique proof the
        // lazy React Flow chunk and the palette both mounted.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "+ Dataset source" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "+ Transformer" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "+ Analyzer" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "+ Dataset sink" }))
            .ToBeVisibleAsync();

        // The "Run history" toolbar button is unique to the editor and
        // sits next to Save / Run — confirms the editor shell is fully
        // wired, not just the palette pane.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "Run history" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pipelines_CronBuilder_PresetSelectionRendersNextRunsPreview()
    {
        // Audit fix archived-12 — schedule fields used to be raw TextInputs;
        // users had to remember the v1 backend's "*/N * * * *" parser
        // quirk. The new CronExpressionBuilder offers presets that
        // produce supported forms and shows the next 3 firings inline
        // so the user can see when the schedule will actually run.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/pipelines");
        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var name = TestNames.Prefixed("cron");
        await modal.GetByLabel("Name").FillAsync(name);

        // Default "Manual only" — no preview alert shows. The Schedule
        // select is rendered by CronExpressionBuilder via its labelled
        // NativeSelect.
        var scheduleSelect = modal.GetByLabel("Schedule", new() { Exact = true });
        await Assertions.Expect(scheduleSelect).ToBeVisibleAsync();
        await Assertions.Expect(modal.GetByText("Next runs")).Not.ToBeVisibleAsync();

        // Pick "Every 5 minutes" → the preview alert mounts.
        await scheduleSelect.SelectOptionAsync("*/5 * * * *");
        await Assertions.Expect(modal.GetByText("Next runs"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(modal.GetByText("Every 5 minutes."))
            .ToBeVisibleAsync();

        // Switch to "Custom (advanced)" and type a daily cron — the
        // backend's parser doesn't support it, so the warning alert
        // mounts instead of the preview.
        await scheduleSelect.SelectOptionAsync("__custom__");
        var customField = modal.GetByLabel("Custom cron expression");
        await Assertions.Expect(customField).ToBeVisibleAsync();
        await customField.FillAsync("0 9 * * *");
        await Assertions.Expect(modal.GetByText("Won't trigger"))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Drop back to a supported form so the create succeeds with a
        // schedule the scheduler will actually fire.
        await scheduleSelect.SelectOptionAsync("*/15 * * * *");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The list row's Schedule column renders the cron in a light
        // Badge — same assertion as Pipelines_CreateWithSchedule_…
        // but proves the preset round-trip persisted.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(row.GetByText("*/15 * * * *", new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pipelines_CreateWithSchedule_RendersCronBadge()
    {
        // Audit fix archived-2 (Pipeline schedule UI) — backend has accepted
        // scheduleCron on create since Phase 5 but the SPA create modal
        // had no input for it, so no pipeline could ever run on a
        // schedule. This test proves the create dialog round-trips the
        // value end-to-end and the list page renders the cron badge.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/pipelines");

        var name = TestNames.Prefixed("sched");
        const string cron = "*/5 * * * *";

        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(name);
        // The Schedule field is now driven by CronExpressionBuilder
        // (audit fix archived-12); the "*/5 * * * *" preset is value-equal to
        // the literal cron string we want to land in the DB.
        await modal.GetByLabel("Schedule", new() { Exact = true }).SelectOptionAsync(cron);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(modal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The Schedule column on PipelinesPage renders the value inside a
        // Mantine Badge (variant="light"). Scope to the row that has the
        // unique name so we don't collide with any stale rows.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(row.GetByText(cron, new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task PipelineEditor_SettingsModal_PersistsScheduleAndRenamesBadge()
    {
        // Audit fix archived-2 (Pipeline schedule UI) — the editor's existing
        // updateMutation only sent { graph }, so name/description/
        // scheduleCron were unreachable post-create. The new "Settings"
        // toolbar button opens a modal that PUTs just those three fields.
        // This test creates a pipeline with no schedule, opens Settings,
        // renames it, sets a cron, saves, and asserts both the inline
        // toolbar badge and the list page reflect the change.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/pipelines");

        var originalName = TestNames.Prefixed("pipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(originalName);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Drill into the editor via the row's name link.
        await page.GetByText(originalName).First.ClickAsync();
        await page.WaitForURLAsync("**/pipelines/*", new() { Timeout = 15_000 });

        // Before edit: toolbar shows the "manual" pill (no cron), confirming
        // the inline status badge is wired to scheduleCron === null.
        await Assertions.Expect(page.GetByText("manual", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Scope the "Settings" lookup to <main>: the top-bar icon menu has
        // aria-label="Settings", so an unscoped role+name match hits two
        // elements and trips strict-mode.
        var main = page.GetByRole(AriaRole.Main);
        await main.GetByRole(AriaRole.Button, new() { Name = "Settings" }).ClickAsync();
        var settings = page.GetByRole(AriaRole.Dialog, new() { Name = "Pipeline settings" });
        await Assertions.Expect(settings).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The modal pre-fills from the loaded pipeline data. Asserting on
        // the name field's value proves openSettings() ran the prefill.
        await Assertions.Expect(settings.GetByLabel("Name")).ToHaveValueAsync(originalName);

        var newName = $"{originalName}-renamed";
        // Use a preset value so the CronExpressionBuilder NativeSelect
        // can hit it directly. "*/15 * * * *" is the "Every 15 minutes"
        // preset (audit fix archived-12).
        const string cron = "*/15 * * * *";
        await settings.GetByLabel("Name").FillAsync(newName);
        await settings.GetByLabel("Description").FillAsync("Scheduled by E2E");
        await settings.GetByLabel("Schedule", new() { Exact = true }).SelectOptionAsync(cron);
        await settings.GetByRole(AriaRole.Button, new() { Name = "Save settings" }).ClickAsync();
        await Assertions.Expect(settings).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // After save: the toolbar Title reflects the new name and the
        // inline schedule badge swapped from "manual" to the cron value.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = newName, Level = 2 }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText(cron, new() { Exact = true }).First)
            .ToBeVisibleAsync();

        // Navigate back to the list and confirm the rename + cron
        // persisted across the round-trip (proves both queries got
        // invalidated, not just the editor's local state).
        await page.GetByRole(AriaRole.Button, new() { Name = "Back to list" }).ClickAsync();
        await page.WaitForURLAsync("**/pipelines", new() { Timeout = 15_000 });
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = newName });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(row.GetByText(cron, new() { Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task PipelineEditor_TransformerNode_PicksBuiltinAndRendersSchemaForm()
    {
        // Audit fix archived-7 — the node-config drawer used to be a freeform
        // JSON Textarea regardless of which of the 14 built-in
        // transformers the node referenced. The fix surfaces
        // /api/transformers/{key}/schema and renders per-builtin form
        // fields. This test drives the happy path: pick "Filter rows",
        // assert its three declared fields (Column, Operator, Value)
        // appear and the JSON Textarea fallback is gone.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/pipelines");

        // Seed a pipeline and drill into the editor.
        var pipelineName = TestNames.Prefixed("pipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(pipelineName);
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByText(pipelineName).First.ClickAsync();
        await page.WaitForURLAsync("**/pipelines/*", new() { Timeout = 15_000 });

        // Add a transformer node — the palette button mounts the node
        // and selects it, surfacing the config drawer on the right.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "+ Transformer" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Transformer" }).ClickAsync();

        // Before a key is picked, the JSON textarea fallback is the
        // only config control — the schema query is disabled (key === "").
        await Assertions.Expect(page.GetByLabel("Config (JSON)")).ToBeVisibleAsync();

        // Pick "Filter rows" from the Transformer dropdown. The schema
        // query fires for "filter-rows" and the drawer swaps the JSON
        // textarea for the form.
        var transformerSelect = page.GetByLabel("Transformer", new() { Exact = true });
        await Assertions.Expect(transformerSelect).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await transformerSelect.SelectOptionAsync(new SelectOptionValue { Value = "filter-rows" });

        // After the schema arrives, the form fields take over and the
        // JSON textarea is gone. Each schema field renders with its
        // declared label — "Column" (required text), "Operator" (select),
        // "Value" (required text). The required ones get an asterisk
        // appended to the label.
        await Assertions.Expect(page.GetByLabel("Config (JSON)")).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Column *")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByLabel("Operator")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Value *")).ToBeVisibleAsync();

        // The Operator select pre-fills with the schema's defaultValue
        // ("=="). Asserting the selected option's value proves the
        // schema's defaults flow through to the rendered control.
        await Assertions.Expect(page.GetByLabel("Operator")).ToHaveValueAsync("==");

        // Type into the Column field — proves the form is editable
        // and the round-trip into `selectedNode.data.config` works
        // (no visible "Save" assertion needed; the save mutation only
        // fires when the user clicks Save, but the input retaining its
        // value across a re-render is enough to know the state wired up).
        await page.GetByLabel("Column *").FillAsync("status");
        await Assertions.Expect(page.GetByLabel("Column *")).ToHaveValueAsync("status");
    }

    [Fact]
    public async Task PipelineEditor_TransformerDropdown_IncludesCodeTransformers()
    {
        // Audit fix archived-3 — Phase 6 user-authored code transformers were
        // unreachable from a pipeline node because the editor's Transformer
        // / Analyzer NativeSelect was populated only from listTransformers()
        // / listAnalyzers() (built-ins). Backend's TransformerNodeRunner
        // already resolves a node's `key` against the built-in registry
        // first and falls through to the code-transformer store by name, so
        // unifying the dropdown content is the whole fix on the SPA side.
        // This test creates a JS transformer through the same UI flow
        // CodeTransformers_CreateJsTransformer_AppearsInList uses, then
        // creates a pipeline, adds a Transformer node, and asserts the
        // code transformer's name appears as an option with the "(code)"
        // suffix that disambiguates it from built-ins.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // 1. Seed a JS code transformer via /code-transformers.
        await page.GotoAsync("/code-transformers");
        var codeName = TestNames.Prefixed("code");
        await page.GetByRole(AriaRole.Button, new() { Name = "New code transformer" }).ClickAsync();
        var codeModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(codeModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await codeModal.GetByLabel("Name").FillAsync(codeName);
        await codeModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(page.GetByText(codeName).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // 2. Create a pipeline and drill into the editor.
        await page.GotoAsync("/pipelines");
        var pipelineName = TestNames.Prefixed("pipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();
        var pipelineModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(pipelineModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await pipelineModal.GetByLabel("Name").FillAsync(pipelineName);
        await pipelineModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(pipelineModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByText(pipelineName).First.ClickAsync();
        await page.WaitForURLAsync("**/pipelines/*", new() { Timeout = 15_000 });

        // 3. Add a transformer node. The editor's palette button adds a node
        // and selects it; the right-side panel renders the config form with
        // the Transformer NativeSelect.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "+ Transformer" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Transformer" }).ClickAsync();

        // 4. Assert the dropdown contains the code transformer with the
        // "(code)" suffix. NativeSelect renders <select><option/></select>,
        // so the option is queryable directly. Scope to the labeled select
        // to avoid colliding with anything else on the page.
        var transformerSelect = page.GetByLabel("Transformer", new() { Exact = true });
        await Assertions.Expect(transformerSelect).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(
            transformerSelect.Locator("option").Filter(new() { HasText = $"{codeName} (code)" }))
            .ToBeAttachedAsync(new() { Timeout = 15_000 });

        // 5. Confirm a built-in still appears alongside (the unified list
        // appends code transformers; built-ins stay reachable). The option's
        // text is the DisplayName, not the Key — "Filter rows" maps to the
        // "filter-rows" registry entry from FilterRowsTransformer.cs.
        await Assertions.Expect(
            transformerSelect.Locator("option").Filter(new() { HasText = "Filter rows" }))
            .ToBeAttachedAsync();
    }

    [Fact]
    public async Task PipelineRun_StepLogs_RenderInExpandedPanelAfterFailedRun()
    {
        // Audit fix archived-11 — step rows used to show only status + rowCount
        // + errorMessage. The orchestrator now buffers per-step log
        // entries (start, success/cancel/fail boundary, full exception
        // type + message + clipped stack on failure) and the SPA
        // renders them in an expanding panel below the step table.
        //
        // We seed a pipeline whose single transformer references an
        // unknown key — the runner falls through to the code-transformer
        // store, also misses, then throws InvalidOperationException at
        // runtime. The worker picks it up within ~5s and Fails the run;
        // the captured logs include the orchestrator's "Starting" entry
        // and the failure entry.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var apiRequest = page.APIRequest;
        var pipelineName = TestNames.Prefixed("steplogs");
        var nodeId = "node-bad-1";

        var graph = new
        {
            nodes = new[]
            {
                new
                {
                    id = nodeId,
                    kind = "transformer",
                    key = "no-such-transformer-xyz",
                    config = (object?)null,
                    position = new { x = 0, y = 0 }
                }
            },
            edges = Array.Empty<object>()
        };

        var createResp = await apiRequest.PostAsync("/api/pipelines/",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    name = pipelineName,
                    description = "Audit fix archived-11 step logs test.",
                    graph
                }
            });
        Assert.True(createResp.Ok, $"create pipeline failed: {createResp.Status}");
        using var createDoc = System.Text.Json.JsonDocument.Parse(await createResp.TextAsync());
        var pipelineId = createDoc.RootElement.GetProperty("id").GetString();

        var runResp = await apiRequest.PostAsync($"/api/pipelines/{pipelineId}/run");
        Assert.True(runResp.Ok, $"enqueue run failed: {runResp.Status}");
        using var runDoc = System.Text.Json.JsonDocument.Parse(await runResp.TextAsync());
        var runId = runDoc.RootElement.GetProperty("id").GetString();

        // Poll the API directly for the run to reach Failed — the worker
        // ticks every 5s and the orchestrator runs synchronously on the
        // worker thread, so a 30s ceiling is comfortable. Skipping the
        // SPA poll for this assertion avoids racing the auto-refresh
        // and gives a clean signal that the orchestrator actually
        // processed the run.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? observedStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var statusResp = await apiRequest.GetAsync(
                $"/api/pipelines/{pipelineId}/runs/{runId}");
            if (statusResp.Ok)
            {
                using var statusDoc = System.Text.Json.JsonDocument.Parse(await statusResp.TextAsync());
                observedStatus = statusDoc.RootElement.GetProperty("run")
                    .GetProperty("status").GetString();
                if (observedStatus == "Failed" || observedStatus == "Succeeded")
                    break;
            }
            await Task.Delay(1_000);
        }
        Assert.Equal("Failed", observedStatus);

        await page.GotoAsync($"/pipelines/{pipelineId}/runs");
        var main = page.GetByRole(AriaRole.Main);
        await Assertions.Expect(
            main.GetByText("Failed", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Click the run row to expand step detail. The rows now carry a
        // data-testid keyed on the run id, so this asks for "a run row"
        // rather than "the first <tr> in the first <table>" — the old form
        // silently retargeted if the table order or sort changed (archived-92).
        await main.Locator("[data-testid^='pipeline-run-row-']").First.ClickAsync();

        // The Steps panel mounts under the runs table. Wait for the single bad
        // node's row to appear.
        //
        // Scoped to a step row rather than to the whole main region: the runs
        // table carries the same text in its errorMessage column, so an
        // unscoped match is ambiguous the moment both tables are on screen —
        // a strict-mode violation rather than a wrong answer, and it only
        // surfaced once CI rendered both at once.
        await Assertions.Expect(
            main.Locator("[data-testid^='pipeline-run-step-']")
                .Filter(new() { HasText = "no-such-transformer-xyz" })
                .First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The Logs column renders a badge with the entry count. Click the
        // step row to expand the logs panel. The step rows have their own
        // testid prefix, so there is no need to reach for "the second table
        // on the page" — the old form also risked matching the runs row,
        // whose errorMessage column contains the same text.
        await main.Locator("[data-testid^='pipeline-run-step-']").First.ClickAsync();

        // The Step logs panel mounts below the step table with
        // aria-label="Step logs" and renders one row per entry. We
        // match the orchestrator's stable start-entry prefix to prove
        // the boundary entry was captured.
        var logPanel = page.GetByLabel("Step logs");
        await Assertions.Expect(logPanel).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(
            logPanel.GetByText(new System.Text.RegularExpressions.Regex("Starting node 'node-bad-1'")))
            .ToBeVisibleAsync();
        // The failure entry leads with the exception type the runner
        // throws (InvalidOperationException). Asserting on the type
        // name keeps the test stable against minor message wording
        // changes.
        await Assertions.Expect(
            logPanel.GetByText(new System.Text.RegularExpressions.Regex("InvalidOperationException")))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task PipelineRun_CancelAndRetry_FlipsStatusAndEnqueuesNewRun()
    {
        // Audit fix archived-10 — cancel and retry endpoints + SPA action icons.
        // We seed all setup through the REST API so the test isn't
        // racing the 5s PipelineRunWorker poll:
        //   1. Create pipeline (empty graph — orchestrator runs no-op)
        //   2. POST /run, immediately POST /cancel before the worker
        //      picks the row up. The store flips Queued → Cancelled and
        //      DequeueOldestAsync filters Queued, so the run never
        //      executes.
        //   3. Navigate to the SPA's runs page, assert the Cancelled
        //      row's Retry icon is visible (Cancel icon is gone).
        //   4. Click Retry → a new row appears via auto-poll.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var apiRequest = page.APIRequest;
        var pipelineName = TestNames.Prefixed("cancel-retry");

        // Create pipeline directly so we don't depend on editor mounting.
        var createResp = await apiRequest.PostAsync("/api/pipelines/",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    name = pipelineName,
                    description = "Audit fix archived-10 cancel/retry test.",
                    graph = new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() }
                }
            });
        Assert.True(createResp.Ok, $"create pipeline failed: {createResp.Status} {await createResp.TextAsync()}");
        using var createDoc = System.Text.Json.JsonDocument.Parse(await createResp.TextAsync());
        var pipelineId = createDoc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(pipelineId));

        // Enqueue a run, then cancel it before the worker dequeues
        // (5s poll window; the back-to-back POSTs land well under that).
        var runResp = await apiRequest.PostAsync($"/api/pipelines/{pipelineId}/run");
        Assert.True(runResp.Ok, $"enqueue run failed: {runResp.Status}");
        using var runDoc = System.Text.Json.JsonDocument.Parse(await runResp.TextAsync());
        var runId = runDoc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(runId));

        var cancelResp = await apiRequest.PostAsync(
            $"/api/pipelines/{pipelineId}/runs/{runId}/cancel");
        Assert.True(cancelResp.Ok, $"cancel run failed: {cancelResp.Status}");

        // Now drive the SPA's run-history view. The runs table auto-
        // polls every 2s when there's nothing busy and on first paint;
        // the Cancelled row should show up immediately.
        await page.GotoAsync($"/pipelines/{pipelineId}/runs");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
                new() { NameRegex = new System.Text.RegularExpressions.Regex($"Run history — {pipelineName}") }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The Cancelled badge appears in the Status cell. Asserting on
        // the Cancelled badge is more stable than "any cell" because
        // the row's queuedAtUtc / completedAtUtc text changes with the
        // user's locale. Scoping to <main> keeps us out of the
        // ConsoleErrorGuard'd top nav.
        var main = page.GetByRole(AriaRole.Main);
        await Assertions.Expect(
            main.GetByText("Cancelled", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Retry ActionIcon mounts on Failed/Cancelled rows; Cancel
        // ActionIcon only on Queued/Running. Both are aria-labelled
        // with the run id so we can locate them precisely.
        var retryButton = page.GetByRole(AriaRole.Button, new() { Name = $"Retry run {runId}" });
        await Assertions.Expect(retryButton).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = $"Cancel run {runId}" }))
            .Not.ToBeVisibleAsync();

        // Click Retry — the SPA POSTs /runs/{id}/retry which enqueues a
        // fresh run with the original graph snapshot. The table's auto-
        // poll picks up the new row within 2s. We assert at least two
        // total Cancelled / non-Cancelled rows by waiting for two
        // distinct status badges, or simply for at least one new status
        // value to render (the new row starts as Queued/Running before
        // the empty-graph orchestrator marks it Succeeded).
        await retryButton.ClickAsync();
        await Assertions.Expect(
            main.GetByText(
                new System.Text.RegularExpressions.Regex("Queued|Running|Succeeded")).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ---- Code Transformers ---------------------------------------------

    [Fact]
    public async Task CodeTransformers_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/code-transformers");

        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Code Transformers", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(
            page.GetByRole(AriaRole.Button, new() { Name = "New code transformer" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task CodeTransformers_CreateJsTransformer_AppearsInList()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/code-transformers");

        var name = TestNames.Prefixed("code");
        await page.GetByRole(AriaRole.Button, new() { Name = "New code transformer" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Kind=transformer + Language=js are the defaults; the JS
        // transformer starter scaffold pre-fills the Code editor so
        // the user can submit immediately. We rely on those defaults.
        await modal.GetByLabel("Name").FillAsync(name);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // After save the modal closes and the new row appears with the unique
        // name.
        //
        // This used to also assert a "Sandboxed" badge. That column is gone
        // (#190): with the is_unsafe flag removed there is nothing it could
        // report but one constant value, and a column that can only say one
        // thing is noise rather than reassurance.
        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task CodeTransformers_CreateModal_MountsCodeMirrorAndOmitsTestPanel()
    {
        // Audit fix archived-5 — the code area is now a CodeMirror editor
        // (line numbers, syntax highlighting) rather than a plain
        // Mantine Textarea. The wrapping Box exposes aria-label="Code
        // editor"; the editor itself renders under .cm-editor. The test
        // panel is only meaningful for saved rows (the test endpoint
        // requires a row id), so it must be absent in create mode.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/code-transformers");

        await page.GetByRole(AriaRole.Button, new() { Name = "New code transformer" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Assertions.Expect(modal.GetByLabel("Code editor")).ToBeVisibleAsync();
        // CodeMirror 6's editor root carries the `.cm-editor` class. If
        // we ever swap to Monaco, this selector needs to change.
        await Assertions.Expect(modal.Locator(".cm-editor")).ToBeVisibleAsync();
        // Test panel hidden in create mode.
        await Assertions.Expect(
            modal.GetByRole(AriaRole.Button, new() { Name = "Run test" }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task CodeTransformers_EditModal_RendersTestPanelAndPyodideHintForPython()
    {
        // Audit fix archived-5 — opening an existing row via the Edit ActionIcon
        // surfaces the Test run panel below the editor (input + config
        // textareas + Run test button). Python-language rows also
        // surface a Pyodide cold-start hint so authors aren't surprised
        // by a 10s first-run delay. We don't actually click "Run test"
        // here because the executor sidecar isn't running in the E2E
        // fixture and we'd burn the 30s timeout for no signal.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GotoAsync("/code-transformers");

        // Seed a Python analyzer (covers the kind=analyzer + Pyodide
        // hint paths in one row).
        var name = TestNames.Prefixed("code");
        await page.GetByRole(AriaRole.Button, new() { Name = "New code transformer" }).ClickAsync();
        var createModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(createModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await createModal.GetByLabel("Name").FillAsync(name);
        await createModal.GetByLabel("Kind").SelectOptionAsync("analyzer");
        await createModal.GetByLabel("Language").SelectOptionAsync("python");
        await createModal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Assertions.Expect(createModal).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Re-open via the row's Edit ActionIcon.
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = $"Edit {name}" }).ClickAsync();

        var editModal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(editModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Test panel is visible only in edit mode.
        await Assertions.Expect(
            editModal.GetByRole(AriaRole.Button, new() { Name = "Run test" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(editModal.GetByLabel("Sample input (JSON)"))
            .ToBeVisibleAsync();
        await Assertions.Expect(editModal.GetByLabel("Test config (JSON)"))
            .ToBeVisibleAsync();

        // Pyodide cold-start hint renders for Python rows. Plain "Pyodide"
        // collides with the Language dropdown's "Python (Pyodide)" option;
        // match on "cold-starts" instead — unique to the hint paragraph.
        await Assertions.Expect(editModal.GetByText("cold-starts", new() { Exact = false }))
            .ToBeVisibleAsync();
    }
}
