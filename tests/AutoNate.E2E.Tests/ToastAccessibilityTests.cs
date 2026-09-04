using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The shared toast wrapper's accessibility behaviour (#89).
/// </summary>
/// <remarks>
/// Asserted through the accessibility tree — roles — rather than by CSS
/// selector, because the tree is what an assistive technology sees and the
/// colour is exactly what it does not get.
///
/// #89's test plan asks for component tests as well. The SPA has no unit test
/// runner (no vitest, no testing-library), and adding one is new infrastructure
/// rather than part of this story, so the same properties are asserted here
/// against the real application instead of a simulated DOM. That is a
/// deviation, recorded in .n8/decisions.md — and arguably the stronger of the
/// two, since a jsdom component test can only confirm the props that were
/// passed, not the role the browser actually computed.
/// </remarks>
public sealed class ToastAccessibilityTests : E2ETestBase
{
    public ToastAccessibilityTests(AutoNateE2EFixture fixture) : base(fixture) { }

    /// <summary>
    /// An error toast is announced assertively and does not disappear.
    /// </summary>
    /// <remarks>
    /// The two properties that matter most, and the two the wrapper had to
    /// override Mantine's defaults to get: an error announced politely can be
    /// missed entirely, and one that vanishes before it is read is worse than
    /// no error.
    /// </remarks>
    [Fact]
    public async Task AnErrorToast_IsAnnouncedAssertively_AndDoesNotAutoDismiss()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await RaiseToastAsync(page, "error", "Something went wrong while saving.");

        // role="alert" is an implicit aria-live="assertive" region.
        var alert = page.GetByRole(AriaRole.Alert)
            .Filter(new() { HasText = "Something went wrong while saving." });
        await Assertions.Expect(alert).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Still there well past every other severity's timeout. A success toast
        // would be long gone by now.
        await page.WaitForTimeoutAsync(6_500);
        await Assertions.Expect(alert).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ASuccessToast_IsAnnouncedPolitely()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await RaiseToastAsync(page, "success", "Saved.");

        // role="status" is an implicit polite live region: it waits for a pause
        // rather than interrupting, which is right for confirming something
        // worked and wrong for telling someone it did not.
        var status = page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved." });
        await Assertions.Expect(status).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // And it is not an alert — the severities must be distinguishable to a
        // screen reader, which is the whole point of the split.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Saved." }))
            .ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AToast_IsDismissibleFromTheKeyboard()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await RaiseToastAsync(page, "error", "Dismiss me with the keyboard.");

        var alert = page.GetByRole(AriaRole.Alert)
            .Filter(new() { HasText = "Dismiss me with the keyboard." });
        await Assertions.Expect(alert).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // The close button is reachable and operable without a mouse. Activated
        // with the keyboard rather than ClickAsync, so this fails if the
        // control is ever swapped for something that only responds to a click.
        var close = alert.GetByRole(AriaRole.Button);
        await close.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(alert).ToHaveCountAsync(0, new() { Timeout = 5_000 });
    }

    [Fact]
    public async Task AToast_DoesNotStealFocus()
    {
        // Feedback on an action must not take the user out of what they were
        // doing. The live region is what carries the message.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/admin/config");
        await page.EvaluateAsync(
            "() => { const el = document.createElement('input');" +
            "  el.id = 'focus-probe'; document.body.appendChild(el); el.focus(); }");

        await RaiseToastAsync(page, "error", "Focus should not move.");
        await Assertions.Expect(
            page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Focus should not move." }))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });

        var focusedId = await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");
        Assert.Equal("focus-probe", focusedId);
    }

    /// <summary>
    /// Raises a toast through the application's own wrapper.
    /// </summary>
    /// <remarks>
    /// Driven through the real module rather than by finding a UI action that
    /// happens to produce one, so the test exercises the wrapper's
    /// configuration directly and does not break when an unrelated page's copy
    /// changes. The module is reached through the Vite dev/preview module graph
    /// the app already loads.
    /// </remarks>
    private static async Task RaiseToastAsync(IPage page, string severity, string message)
    {
        if (page.Url is null || !page.Url.Contains("/admin", StringComparison.Ordinal))
        {
            await page.GotoAsync("/admin/config");
        }

        await page.EvaluateAsync(
            @"async ([severity, message]) => {
                const mod = await import('/src/components/notifications/toast.ts');
                mod.toast[severity](message);
            }",
            new[] { severity, message });
    }
}
