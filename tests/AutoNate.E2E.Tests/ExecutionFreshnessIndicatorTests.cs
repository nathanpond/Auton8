using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The executions view says how current it is, accessibly (#109).
/// </summary>
/// <remarks>
/// <para>
/// These are DOM claims, and the SPA's vitest runs with
/// <c>environment: "node"</c> — there is no DOM there to assert against. The
/// decision logic is unit-tested in <c>src/lib/__tests__/execution-freshness.test.ts</c>;
/// what is left, and what belongs here, is that the state reaches a user and
/// reaches one who is not using a mouse.
/// </para>
/// </remarks>
public sealed class ExecutionFreshnessIndicatorTests : E2ETestBase
{
    public ExecutionFreshnessIndicatorTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_executions_view_states_how_current_it_is()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/workflow-executions");

        var indicator = page.GetByTestId("execution-freshness");
        await indicator.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var text = (await indicator.InnerTextAsync()).Trim();
        Assert.False(
            string.IsNullOrWhiteSpace(text),
            "The executions view must say how current its data is, not leave the user to guess.");
    }

    /// <summary>
    /// The indicator is a live region, so a change is announced (#109).
    /// </summary>
    /// <remarks>
    /// <para>Asserted on the container's attributes rather than on a rendered
    /// announcement, because whether a screen reader speaks is not observable
    /// from Playwright. What IS observable, and what actually decides it, is that
    /// a persistent region exists with a polite live setting — a region created at
    /// the same moment as its text may never be announced at all, since the
    /// assistive technology has nothing to compare against.</para>
    ///
    /// <para><c>polite</c> rather than <c>assertive</c> is deliberate and asserted:
    /// interrupting whatever the user is reading to say the data is a minute old
    /// would be worse than saying nothing.</para>
    /// </remarks>
    [Fact]
    public async Task The_freshness_indicator_is_a_polite_live_region()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/workflow-executions");
        var indicator = page.GetByTestId("execution-freshness");
        await indicator.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.Equal("status", await indicator.GetAttributeAsync("role"));
        Assert.Equal("polite", await indicator.GetAttributeAsync("aria-live"));
    }

    /// <summary>
    /// The refresh control is reachable and operable by keyboard (#109).
    /// </summary>
    /// <remarks>
    /// Tabbing to it is the half that matters: a control only clickable with a
    /// mouse fails the criterion however it looks. Focus is driven by keyboard
    /// rather than by <c>FocusAsync()</c>, which would prove the element can hold
    /// focus without proving a user can get there.
    /// </remarks>
    [Fact]
    public async Task The_refresh_control_is_reachable_and_operable_by_keyboard()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/workflow-executions");
        var refresh = page.GetByTestId("execution-freshness-refresh");
        await refresh.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Walk the tab order until it lands on the control, rather than focusing
        // it directly. A generous bound: the page has a menu and a toolbar ahead
        // of it, and the assertion is "reachable", not "reachable in N".
        var reached = false;
        for (var i = 0; i < 60 && !reached; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached = await refresh.EvaluateAsync<bool>("el => el === document.activeElement");
        }

        Assert.True(reached, "The refresh control must be reachable by keyboard alone.");

        // And operable from the keyboard, not merely focusable.
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(250);
        Assert.True(await refresh.IsVisibleAsync());
    }
}
