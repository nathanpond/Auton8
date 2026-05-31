using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Smoke + create-modal + editor-mounts coverage for the Phase 5 + Phase 6
/// admin pages of the Data Stores &amp; Analytics Pipeline plan
/// (<c>docs/plans/2026-05-30-data-stores-implementation.md</c>):
/// <c>/admin/config/pipelines</c>, the lazy-loaded React Flow editor at
/// <c>/admin/config/pipelines/{id}</c>, and <c>/admin/config/code-transformers</c>.
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

        await page.GotoAsync("/admin/config/pipelines");

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
        await page.GotoAsync("/admin/config/pipelines");

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
        await page.GotoAsync("/admin/config/pipelines");

        var name = TestNames.Prefixed("pipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "New pipeline" }).ClickAsync();
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await modal.GetByLabel("Name").FillAsync(name);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // The list row links into the editor; clicking the name navigates
        // to /admin/config/pipelines/{id}.
        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByText(name).First.ClickAsync();
        await page.WaitForURLAsync("**/admin/config/pipelines/*", new() { Timeout = 15_000 });

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

    // ---- Code Transformers ---------------------------------------------

    [Fact]
    public async Task CodeTransformers_PageRenders_WithHeadingAndNewButton()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config/code-transformers");

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
        await page.GotoAsync("/admin/config/code-transformers");

        var name = TestNames.Prefixed("code");
        await page.GetByRole(AriaRole.Button, new() { Name = "New code transformer" }).ClickAsync();

        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Kind=transformer + Language=js are the defaults; the JS
        // transformer starter scaffold pre-fills the Code textarea so
        // the user can submit immediately. We rely on those defaults.
        await modal.GetByLabel("Name").FillAsync(name);
        await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

        // After save the modal closes and the new row appears with the
        // unique name. The DataTable renders "Sandboxed" as a green badge
        // when is_unsafe = false (the default).
        await Assertions.Expect(page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText("Sandboxed").First).ToBeVisibleAsync();
    }
}
