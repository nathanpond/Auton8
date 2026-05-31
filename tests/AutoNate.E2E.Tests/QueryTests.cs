using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Covers the dynamic <c>/query</c> page's AQL and saved-query lifecycle.
/// </summary>
public sealed class QueryTests : E2ETestBase
{
    public QueryTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task QueryPage_ExecuteSaveReloadAndLoadSavedQuery()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var queryName = TestNames.Prefixed("saved-query");

        await page.GotoAsync("/query");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
                new() { Name = "Query", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();
        var saveButton = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Assertions.Expect(saveButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await saveButton.ClickAsync();

        var saveDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Save query" });
        await saveDialog.GetByLabel("Name").FillAsync(queryName);
        await saveDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(saveDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.ReloadAsync();
        var savedQueries = page.GetByRole(AriaRole.Combobox, new() { Name = "Saved queries" });
        await Assertions.Expect(savedQueries).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await savedQueries.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = queryName, Exact = true }).ClickAsync();

        await Assertions.Expect(savedQueries).ToHaveValueAsync(queryName);
    }

    [Fact]
    public async Task QueryPage_InvalidAql_RendersValidationError()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.GotoAsync("/query");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
                new() { Name = "Query", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var editor = page.Locator(".cm-content");
        await editor.ClickAsync();
        await editor.PressAsync("ControlOrMeta+A");
        await editor.PressSequentiallyAsync("NOT VALID AQL");
        await page.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();

        var alert = page.GetByRole(AriaRole.Alert);
        await Assertions.Expect(alert).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(alert).ToContainTextAsync("Query errors");
    }
}
