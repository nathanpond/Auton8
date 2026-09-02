using AutoNate.E2E.Tests.Support;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.E2E.Tests;

// Automated WCAG scan over the pages a signed-in user actually lives on (archived-40).
//
// This is the permanent version of the 508 findings fixed by hand in archived-7–archived-17:
// eslint catches markup patterns in source, axe catches what the rendered DOM
// actually exposes — a colour pair that fails, a control with no accessible
// name, a landmark that never got labelled. Neither substitutes for the
// other.
//
// Scope is deliberately narrow: `critical` and `serious` impacts only, on the
// wcag2a / wcag2aa tag sets. `moderate` and `minor` include advisory findings
// (best-practice landmark structure, heading order) that would make this a
// noise generator rather than a gate. A gate nobody trusts gets skipped.
[Collection(AutoNateE2ECollection.Name)]
public sealed class AxeAccessibilitySmokeTests : E2ETestBase
{
    private readonly ITestOutputHelper _output;

    public AxeAccessibilitySmokeTests(AutoNateE2EFixture fixture, ITestOutputHelper output)
        : base(fixture) => _output = output;

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/projects", "Projects")]
    [InlineData("/notifications", "Notifications")]
    [InlineData("/admin/config/appearance", "Appearance")]
    public async Task Page_has_no_critical_or_serious_accessibility_violations(
        string path, string label)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync(path);
        // Wait for the shell rather than a page-specific element so this stays
        // usable as new paths are added to the theory.
        await Assertions.Expect(page.Locator("#content"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = ["wcag2a", "wcag2aa"]
            }
        });

        var blocking = results.Violations
            .Where(v => v.Impact is "critical" or "serious")
            .ToArray();

        foreach (var v in blocking)
        {
            _output.WriteLine(
                $"{label} {v.Impact}: {v.Id} — {v.Help} ({v.Nodes.Length} node(s))");
            foreach (var node in v.Nodes.Take(3))
            {
                _output.WriteLine($"    {string.Join(", ", node.Target)}");
                _output.WriteLine($"    HTML: {node.Html}");

            }
        }

        Assert.True(blocking.Length == 0,
            $"{label} has {blocking.Length} critical/serious accessibility violation(s): " +
            string.Join(", ", blocking.Select(v => v.Id)));
    }
}
