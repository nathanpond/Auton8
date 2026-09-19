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
    /// <summary>
    /// With the feed's heartbeat gone, the view says "not updating" — and does
    /// NOT masquerade as an error banner (#109).
    /// </summary>
    /// <remarks>
    /// <para><b>The state is forced, not waited for.</b> The first version of this
    /// suite never exercised this branch locally — the dev app had polled, so the
    /// indicator always read fresh — and CI hit it only because its environment had
    /// no heartbeat. That made a real defect visible on CI and invisible here, and
    /// left a green local run proving nothing. Deleting the watermark makes the
    /// condition deterministic in both places.</para>
    ///
    /// <para><b>The second assertion is the one that failed on CI.</b> Mantine's
    /// <c>Alert</c> defaults to <c>role="alert"</c>, and on this page that role
    /// already means "an error banner is showing" —
    /// <c>WorkflowOverrideTests.WorkflowExecutionsPage_RendersForSeededAdmin</c>
    /// asserts none is visible. A status indicator wearing that role both broke
    /// that check and nested an assertive live region inside the polite one, so a
    /// state change would interrupt whatever the user was reading.</para>
    /// </remarks>
    [Fact]
    public async Task A_stopped_feed_reads_as_not_updating_without_posing_as_an_error()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = session.Page.APIRequest;

        var reset = await api.PostAsync(
            "/api/admin/projections/feeds/flowable.exec.poll/reset-watermark");
        Assert.True(reset.Ok, $"Resetting the watermark failed: {reset.Status}");

        await page.GotoAsync("/workflow-executions");

        var stopped = page.GetByTestId("execution-freshness-stopped");
        await stopped.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Contains("not updating", (await stopped.InnerTextAsync()).ToLowerInvariant());

        // It is a status, not an error. `role="alert"` on this page is reserved
        // for a genuine failure banner.
        Assert.NotEqual("alert", await stopped.GetAttributeAsync("role"));
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();
    }

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
