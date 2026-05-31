using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class FormsAdvancedTests : E2ETestBase
{
    public FormsAdvancedTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task FormEditor_SavesDraftAndRendersDraftPreview()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var marker = TestNames.Prefixed("form-draft-marker");
        var form = await seeder.CreateFormAsync(
            TestNames.Prefixed("draft-form"),
            $"e2e-{TestNames.ShortSlug()}",
            siteAvailable: false);
        await SaveNameAsync(page.APIRequest, form.Id, marker);

        await page.GotoAsync($"/admin/config/forms/{form.Id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = marker }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var previewTask = session.Context.WaitForPageAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Open dev" }).ClickAsync();
        var preview = await previewTask;
        await Assertions.Expect(preview.GetByRole(AriaRole.Heading, new() { Name = "New form" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await preview.CloseAsync();

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByLabel("Name"))
            .ToHaveValueAsync(marker, new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task FormEditor_RestoresVersionAndDeletesForm()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);
        var form = await seeder.CreateFormAsync(
            TestNames.Prefixed("restore-form"),
            $"e2e-{TestNames.ShortSlug()}",
            siteAvailable: true);
        await seeder.PublishFormAsync(form.Id);
        await SaveNameAsync(page.APIRequest, form.Id, TestNames.Prefixed("changed-draft"));

        await page.GotoAsync($"/admin/config/forms/{form.Id}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Versions" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Version history" });
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Restore" }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        Task? acceptDialogTask = null;
        page.Dialog += (_, browserDialog) => acceptDialogTask = browserDialog.AcceptAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Restore" }).First.ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(page.GetByRole(AriaRole.Status))
            .ToHaveTextAsync("Restored — buffer reloaded.", new() { Timeout = 10_000 });

        await page.GotoAsync("/admin/config/forms");
        acceptDialogTask = null;
        await page.GetByRole(AriaRole.Button, new() { Name = $"Delete {form.ShortCode}" }).ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await Assertions.Expect(page.GetByText(form.Name, new() { Exact = true }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    private static async Task SaveNameAsync(IAPIRequestContext request, Guid formId, string name)
    {
        var get = await request.GetAsync($"/api/forms/{formId}");
        Assert.True(get.Ok, await get.TextAsync());
        var form = await get.JsonAsync();
        var save = await request.PutAsync($"/api/forms/{formId}", new()
        {
            DataObject = new
            {
                name,
                shortCode = form!.Value.GetProperty("shortCode").GetString(),
                formCode = form.Value.GetProperty("formCode").GetString(),
                siteAvailable = form.Value.GetProperty("siteAvailable").GetBoolean()
            }
        });
        Assert.True(save.Ok, await save.TextAsync());
    }

}
