using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

// E2E-061 from docs/playwright-test-backlog.md, which sat BLOCKED on "the
// current record seeder creates schema-less record types". ApiSeeder now has
// AddRecordTypeFieldAsync, so a typed schema can be built up front and the
// journey can exercise what typed fields are actually for: entering values of
// a declared type and filtering on them.
[Collection(AutoNateE2ECollection.Name)]
public sealed class RecordsAdvancedTests : E2ETestBase
{
    public RecordsAdvancedTests(AutoNateE2EFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task TypedFields_FilterTheRecordListToMatchingRowsOnly()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var type = await seeder.CreateRecordTypeAsync(
            TestNames.ShortCode(), TestNames.Prefixed("typed"));

        // Three representative shapes rather than every type: a free-text
        // field, a numeric one (different operator set), and an option field
        // (a constrained choice list). Those three cover the branches the
        // filter builder actually switches on.
        await seeder.AddRecordTypeFieldAsync(type.Id, "team", "Team", "text", sortOrder: 0);
        await seeder.AddRecordTypeFieldAsync(type.Id, "score", "Score", "number", sortOrder: 1);
        await seeder.AddRecordTypeFieldAsync(
            type.Id, "tier", "Tier", "option",
            configJson: """
            {"choices":[{"value":"gold","label":"Gold"},{"value":"silver","label":"Silver"}]}
            """,
            sortOrder: 2);

        var matching = TestNames.Prefixed("gold-rec");
        var other = TestNames.Prefixed("silver-rec");
        await seeder.CreateRecordAsync(type.Id, matching,
            valuesJson: """{"team":"platform","score":90,"tier":"gold"}""");
        await seeder.CreateRecordAsync(type.Id, other,
            valuesJson: """{"team":"platform","score":10,"tier":"silver"}""");

        await page.GotoAsync($"/records/{type.ShortCode}");

        // Both rows before filtering — otherwise a filter that hides
        // everything would look like a pass.
        await Assertions.Expect(page.GetByText(matching).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.GetByText(other).First).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Filters" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Add filter" }).ClickAsync();

        // The three controls in a filter row are now named, so this asks for
        // the field/operator/value it means rather than counting selects.
        await page.GetByLabel("Filter 1 field").SelectOptionAsync("tier");
        await page.GetByLabel("Filter 1 value").SelectOptionAsync("gold");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();

        // The typed filter reached the server and narrowed the list: the gold
        // record survives, the silver one is gone.
        await Assertions.Expect(page.GetByText(matching).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.GetByText(other))
            .Not.ToBeVisibleAsync(new() { Timeout = 20_000 });
    }
}
