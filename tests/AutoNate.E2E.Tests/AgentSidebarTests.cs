using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

// Smoke + interaction coverage for the agent sidebar. We don't probe a real
// LLM here — the E2E fixture starts a fresh DB with no External Connection
// rows. The goal is to confirm:
//   1. The toggle is rendered for a signed-in user.
//   2. Clicking it actually opens the side panel.
//   3. The admin "External connections" config page renders and its
//      "New connection" affordance opens the form modal.
// Streaming-with-real-Anthropic flow is a separate fixture concern.
[Collection(AutoNateE2ECollection.Name)]
public sealed class AgentSidebarTests
{
    private readonly AutoNateE2EFixture _fixture;

    public AgentSidebarTests(AutoNateE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Toggle_OpensTheAgentPanel()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await AutoNateE2EFixture.SignInAsAdminAsync(page);

        // The header trigger announces itself via aria-label. Previously the
        // tests grepped for a `.agent-toggle` class, which the Mantine
        // migration deleted.
        var toggle = page.GetByLabel("Open AutoNate assistant");
        await toggle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Before the click the close button shouldn't be visible (the
        // <aside> is rendered but its inner is gated on isOpen). The old
        // "AutoNate Assistant" header text was removed in favor of the
        // current-page breadcrumb (see AgentSidebar.css comment near .136),
        // so the Close-button affordance is now the canonical open-state
        // signal.
        await Assertions.Expect(page.GetByLabel("Close assistant")).Not.ToBeVisibleAsync();

        await toggle.ClickAsync();

        // The opened panel renders a Close button with aria-label="Close
        // assistant". Asserting on it catches drift in either the trigger or
        // the open-state CSS class.
        await Assertions.Expect(page.GetByLabel("Close assistant"))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Fact]
    public async Task ExternalConnectionsAdminPage_OpensNewConnectionModal()
    {
        await using var context = await _fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await AutoNateE2EFixture.SignInAsAdminAsync(page);
        await page.GotoAsync("/admin/config/external-connections");

        // The "New connection" button is the tooltip-wrapped + (plus) icon
        // in the toolbar. It has both aria-label="New connection" and a
        // Mantine Tooltip; we click by the accessible name.
        var newButton = page.GetByRole(AriaRole.Button, new() { Name = "New connection" });
        await Assertions.Expect(newButton).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await newButton.ClickAsync();

        // ConnectionFormModal opens with title "New connection". Asserting on
        // both the dialog role and the Kind selector exercise the full mount,
        // not just the heading.
        var modal = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Assertions.Expect(modal.GetByLabel("Kind")).ToBeVisibleAsync();
    }
}
