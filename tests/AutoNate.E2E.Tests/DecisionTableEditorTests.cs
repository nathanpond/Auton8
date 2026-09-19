using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The decision-table editor, driven the way an author drives it (#110).
/// </summary>
/// <remarks>
/// <para>
/// <b>A grid is the control type most often built mouse-only</b> — the story says
/// so, and the a11y ratchet from #68 applies. So the keyboard walk here is a real
/// walk: the cell is reached by pressing Tab until focus lands on it, never by
/// calling <c>FocusAsync</c>. A control reachable only by a direct focus call is
/// not keyboard-operable, and a click-driven test cannot tell the difference.
/// </para>
/// <para>
/// Untraited, so it runs on every merge. Creating, editing and validating need no
/// engine; publishing does, and that half is covered by
/// <c>GeneratedDecisionTableTests</c>.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class DecisionTableEditorTests : E2ETestBase
{
    public DecisionTableEditorTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_author_creates_a_table_edits_a_cell_by_keyboard_and_sees_a_bad_cell_refused()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var key = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var name = TestNames.Prefixed("decision");

        await page.GotoAsync("/admin/config/decision-tables");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Decision Tables" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "New decision table" }).ClickAsync();
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByLabel("Key").FillAsync(key);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();

        // Creating navigates into the editor, so the author lands where the work is
        // rather than back on a list they then have to search.
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = name }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Give the table a rule to edit.
        await page.GetByRole(AriaRole.Button, new() { Name = "Add rule" }).ClickAsync();

        // The cell is named by RULE and COLUMN, which is also how the server's
        // error messages name it -- so an author reading "Rule 1, Input 1" in an
        // error can find the box it is about.
        var cell = page.GetByLabel("Rule 1, Input 1");
        await Assertions.Expect(cell).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // THE KEYBOARD WALK. Tabbed to, not focused directly.
        var reached = false;
        for (var i = 0; i < 120 && !reached; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached = await cell.EvaluateAsync<bool>("el => el === document.activeElement");
        }

        Assert.True(reached, "The first rule cell could not be reached with Tab alone.");

        // A word in a string column, unquoted. It parses, it would deploy, and it
        // would match nothing at run time -- the silent failure the validator
        // exists for.
        await page.Keyboard.TypeAsync("EU");
        await page.GetByLabel("Rule 1, Output 1").FillAsync("\"eu-desk\"");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // Refused AT SAVE, naming the cell -- not at execution time, where the
        // failure would land on whoever ran the process.
        var problems = page.GetByRole(AriaRole.Alert);
        await Assertions.Expect(problems).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(problems).ToContainTextAsync("Input 1");
        await Assertions.Expect(problems).ToContainTextAsync("variable name");

        // And it says what to write instead. "This is wrong" is half a message.
        await Assertions.Expect(problems).ToContainTextAsync("\"EU\"");

        // Quote it, and the same table saves. Without this half the test passes
        // against a validator that refuses everything.
        await cell.FillAsync("\"EU\"");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(problems).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Adding_a_column_widens_the_rules_that_already_exist()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        var key = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var name = TestNames.Prefixed("widen");

        await page.GotoAsync("/admin/config/decision-tables");
        await page.GetByRole(AriaRole.Button, new() { Name = "New decision table" }).ClickAsync();
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByLabel("Key").FillAsync(key);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = name }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Add rule" }).ClickAsync();
        await Assertions.Expect(page.GetByLabel("Rule 1, Input 1"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Add input" }).ClickAsync();

        // The existing rule grew a cell in the same operation. Adding a column
        // without widening the rules leaves every one of them ragged, the table
        // cannot be saved, and the author is fixing damage the editor did.
        await Assertions.Expect(page.GetByLabel("Rule 1, Input 2"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByLabel("Rule 1, Output 1").FillAsync("\"ok\"");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // Saving a two-input table with a two-cell rule succeeds. If the widening
        // had not happened, the server would refuse it with a count mismatch --
        // which is what this asserts the absence of.
        await Assertions.Expect(page.GetByRole(AriaRole.Alert))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
