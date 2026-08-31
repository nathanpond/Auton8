using AutoNate.E2E.Tests.Support;
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
public sealed class AgentSidebarTests : E2ETestBase
{
    public AgentSidebarTests(AutoNateE2EFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Toggle_OpensTheAgentPanel()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

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
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
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

    // ---- Phase 8 extensions: composer / resize handle / Cmd+K palette ----

    [Fact]
    public async Task OpenSidebar_RendersComposerAcceptingTypedInput()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GetByLabel("Open AutoNate assistant").ClickAsync();

        // The composer placeholder differs by conversation state
        // (AgentSidebar.tsx:507-509): on a fresh open with no active
        // conversation we get "Ask the assistant about this page…".
        var composer = page.GetByPlaceholder("Ask the assistant about this page…");
        await Assertions.Expect(composer).ToBeVisibleAsync(new() { Timeout = 10_000 });

        const string typed = "phase 8 composer smoke";
        await composer.FillAsync(typed);
        await Assertions.Expect(composer).ToHaveValueAsync(typed);
    }

    [Fact]
    public async Task OpenSidebar_RendersResizeHandle()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        await page.GetByLabel("Open AutoNate assistant").ClickAsync();

        // The resize handle (AgentSidebar.tsx:373) is a separator with
        // aria-label="Resize chatbot" — only mounted on the open sidebar
        // because the aside's inner is gated on isOpen.
        await Assertions.Expect(page.GetByLabel("Resize chatbot"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task CmdK_OpensChatPaletteModal_FromAnyPage()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // AgentSidebar.tsx:204-205 attaches a global keydown listener that
        // accepts either Meta+K or Ctrl+K. Use Control+K — Playwright fires
        // the same key event on both macOS and Linux Chromium and the handler
        // accepts either modifier.
        await page.Keyboard.PressAsync("Control+k");

        // ChatPaletteModal.tsx:147 — distinctive placeholder text on the
        // search input, only present when the palette is open.
        await Assertions.Expect(
            page.GetByPlaceholder("Search every chat by title or page…"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
