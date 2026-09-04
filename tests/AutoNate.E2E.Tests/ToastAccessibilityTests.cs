using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The shared toast wrapper's accessibility behaviour (#89).
/// </summary>
/// <remarks>
/// Asserted through roles in the accessibility tree rather than by CSS
/// selector, because the tree is what assistive technology sees and the colour
/// is exactly what it does not get.
///
/// Driven by **real actions** on the Identity Providers screen (#87), as #89's
/// test plan asks. An earlier version reached into the module directly with a
/// dynamic import of `/src/components/notifications/toast.ts`; that resolves
/// under the Vite dev server and not against the built bundle these tests run
/// on, so it failed in CI with "Failed to fetch dynamically imported module".
/// Driving the UI is what the plan meant and is the more honest test anyway —
/// it proves the wrapper is wired into a real page, not merely importable.
///
/// #89 also asks for component tests. The SPA has no unit test runner, and
/// adding one is new infrastructure rather than part of that story; these cover
/// the same properties against the browser's computed roles instead. Recorded
/// in .n8/decisions.md.
/// </remarks>
public sealed class ToastAccessibilityTests : E2ETestBase
{
    public ToastAccessibilityTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private const string ProvidersUrl = "/admin/config/identity-providers";

    [Fact]
    public async Task ASuccessToast_IsAnnouncedPolitely_AndNotAsAnAlert()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var name = $"Polite {Guid.NewGuid():N}"[..20];
        await CreateProviderAsync(page, name);

        // role="status" is an implicit polite live region: it waits for a pause
        // rather than interrupting, which is right for confirming something
        // worked.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Created" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // And explicitly not an alert. The severities have to be
        // distinguishable to a screen reader — that is the whole point of the
        // split, and it is invisible if both render the same role.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Created" }))
            .ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnErrorToast_IsAnnouncedAssertively_AndDoesNotAutoDismiss()
    {
        // The two properties the wrapper had to override Mantine's defaults to
        // get: an error announced politely can be missed entirely, and one that
        // vanishes before it is read is worse than no error.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var name = $"Dup {Guid.NewGuid():N}"[..16];
        await CreateProviderAsync(page, name);
        await Assertions.Expect(
            page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Created" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Same slug again — the backend refuses it with a reason, which the
        // page raises as an error toast.
        await CreateProviderAsync(page, name);

        var alert = page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "slug" });
        await Assertions.Expect(alert).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Still there past every other severity's timeout — the longest is 10s.
        await page.WaitForTimeoutAsync(11_000);
        await Assertions.Expect(alert).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AnErrorToast_IsDismissibleFromTheKeyboard()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var name = $"Kbd {Guid.NewGuid():N}"[..16];
        await CreateProviderAsync(page, name);
        await Assertions.Expect(
            page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Created" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await CreateProviderAsync(page, name);

        var alert = page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "slug" });
        await Assertions.Expect(alert).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Focused and activated with the keyboard rather than clicked, so this
        // fails if the close control is ever swapped for something that only
        // responds to a mouse.
        var close = alert.GetByRole(AriaRole.Button).First;
        await close.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(alert).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Opens the drawer and creates a provider whose slug is derived from
    /// <paramref name="displayName"/>.
    /// </summary>
    private static async Task CreateProviderAsync(IPage page, string displayName)
    {
        await page.GotoAsync(ProvidersUrl);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add provider" })
            .ClickAsync(new() { Timeout = 15_000 });

        await page.GetByLabel("Display name").FillAsync(displayName);
        await page.GetByLabel("Authority").FillAsync("https://idp.example.com/realms/x");
        await page.GetByLabel("Client ID").FillAsync("auton8");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
    }
}
