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
    public async Task QueryPage_DeleteSavedQuery_RemovesItFromTheCombobox()
    {
        // Audit fix archived-8 — `deleteSavedQuery` was unreachable from the SPA
        // until this commit. Save → load → Delete is the only path that
        // surfaces the new toolbar button (it's gated on
        // `selectedQuery && canUpdateSelected`), so the test walks
        // through that whole chain and then asserts the option is gone.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var queryName = TestNames.Prefixed("saved-to-delete");

        await page.GotoAsync("/query");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading,
                new() { Name = "Query", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Execute → Save: the toolbar Save button gates on a successful
        // run, mirroring the existing test's setup.
        await page.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();
        var saveButton = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Assertions.Expect(saveButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await saveButton.ClickAsync();
        var saveDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Save query" });
        await saveDialog.GetByLabel("Name").FillAsync(queryName);
        await saveDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(saveDialog).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Reload before re-selecting — the existing QueryPage_Execute…
        // test follows the same shape and reloads here. Picking the
        // option without a reload sometimes loses the click to the
        // intermediate setQueryData propagation; the reload also proves
        // the row survives a page round-trip.
        await page.ReloadAsync();

        // Load the saved row so the Delete button mounts. The Delete
        // button only renders when `selectedQuery && canUpdateSelected`,
        // matching the existing Share button's visibility gate.
        var savedQueries = page.GetByRole(AriaRole.Combobox, new() { Name = "Saved queries" });
        await Assertions.Expect(savedQueries).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await savedQueries.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = queryName, Exact = true }).ClickAsync();
        await Assertions.Expect(savedQueries).ToHaveValueAsync(queryName);

        // The delete is guarded by window.confirm — auto-accept it so
        // the test can drive the deletion without a UI prompt.
        Task? confirmTask = null;
        page.Dialog += (_, dialog) => confirmTask = dialog.AcceptAsync();

        var deleteButton = page.GetByRole(
            AriaRole.Button, new() { Name = $"Delete saved query {queryName}" });
        await Assertions.Expect(deleteButton).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await deleteButton.ClickAsync();
        if (confirmTask is not null) await confirmTask;

        // After delete, the row's option is gone from the combobox.
        // The Select's value is cleared (deleteMutation.onSuccess
        // clears `selectedQueryId` when the deleted id matched), so the
        // combobox shows the empty-state placeholder again.
        await Assertions.Expect(savedQueries).ToHaveValueAsync("", new() { Timeout = 10_000 });
        await savedQueries.ClickAsync();
        await Assertions.Expect(
            page.GetByRole(AriaRole.Option, new() { Name = queryName, Exact = true }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task QueryPage_IssueShareLink_LandsOnPublicSharedQueryPage()
    {
        // Audit fix archived-9 — share links used to point at
        // /api/public/queries/share/{token}, dropping recipients on raw
        // JSON. The new /q/{token} route is an unauthenticated SPA page
        // that calls the same backend endpoint and renders rows in a
        // DataTable. This test drives the full chain: save → load →
        // share → issue → assert the URL points at /q/ → navigate
        // there → confirm the page mounts with rows.
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var queryName = TestNames.Prefixed("shared");

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

        // Reload (matches the delete test's pattern) so the Combobox
        // re-derives from the network and option-click race conditions
        // around `setQueryData` don't flake.
        await page.ReloadAsync();
        var savedQueries = page.GetByRole(AriaRole.Combobox, new() { Name = "Saved queries" });
        await Assertions.Expect(savedQueries).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await savedQueries.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = queryName, Exact = true }).ClickAsync();
        await Assertions.Expect(savedQueries).ToHaveValueAsync(queryName);

        // Open the share modal — visible because the loaded row is owned.
        await page.GetByRole(AriaRole.Button, new() { Name = "Share", Exact = true }).ClickAsync();
        var shareDialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Share \"{queryName}\"" });
        await Assertions.Expect(shareDialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await shareDialog.GetByRole(AriaRole.Button, new() { Name = "Generate link" }).ClickAsync();

        // After issuance the modal renders the URL as an Anchor inside a
        // teal "Copy this link now" alert. Pull the href off it — the
        // backend's BuildShareUrl now returns /q/{rawToken} after audit
        // fix archived-9 (was /api/public/queries/share/{...}).
        var shareLink = shareDialog.GetByRole(AriaRole.Link).First;
        await Assertions.Expect(shareLink).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var href = await shareLink.GetAttributeAsync("href");
        Assert.NotNull(href);
        Assert.Contains("/q/", href);
        Assert.DoesNotContain("/api/public/queries/share/", href);

        // Navigate to the share URL in the same context. The SPA route
        // is mounted outside ProtectedRoute so the auth cookie isn't
        // required — but the existing cookie doesn't hurt either; the
        // backend authorizes via the share token, not the session.
        await page.GotoAsync(href!);

        // The public page renders "Shared query" as its h1 and a "X rows"
        // badge once the AqlQueryResponse comes back.
        await Assertions.Expect(
            page.GetByRole(AriaRole.Heading, new() { Name = "Shared query", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(
            page.GetByText(new System.Text.RegularExpressions.Regex(@"\d+ rows")))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
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
