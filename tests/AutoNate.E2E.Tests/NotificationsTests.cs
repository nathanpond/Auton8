using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class NotificationsTests : E2ETestBase
{
    public NotificationsTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Notifications_PageShareUnreadFilterMarkAllAndLinkedNavigation()
    {
        await using var admin = await NewSignedInAsAdminAsync();
        var request = admin.Page.APIRequest;
        var seeder = new ApiSeeder(request);
        var user = await seeder.CreateUserAsync(TestNames.Prefixed("notifications"), "Password123!");
        var project = await seeder.CreateProjectAsync(TestNames.Prefixed("notifications-project"));
        var cabinetId = await PostForIdAsync(request, "/api/content/cabinets/", new
        {
            projectId = project.Id,
            name = TestNames.Prefixed("notifications-cabinet")
        });
        var notebookId = await PostForIdAsync(request, "/api/content/notebooks/", new
        {
            cabinetId,
            name = TestNames.Prefixed("notifications-notebook")
        });
        var pageTitle = TestNames.Prefixed("notifications-page");
        var pageResponse = await request.PostAsync("/api/content/pages/", new()
        {
            DataObject = new { notebookId, parentPageId = (Guid?)null, title = pageTitle }
        });
        Assert.True(pageResponse.Ok, await pageResponse.TextAsync());
        var pageJson = await pageResponse.JsonAsync();
        var pageId = pageJson!.Value.GetProperty("id").GetGuid();
        var locator = pageJson.Value.GetProperty("locator").GetInt64();

        for (var i = 0; i < 2; i++)
        {
            var shareResponse = await request.PostAsync($"/api/content/pages/{pageId}/share/", new()
            {
                DataObject = new { userIds = new[] { user.UserId }, grantAccess = true }
            });
            Assert.True(shareResponse.Ok, await shareResponse.TextAsync());
        }

        await using var session = await NewSignedInAsAsync(user.Username, "Password123!");
        var page = session.Page;
        await page.GotoAsync("/notifications");
        await Assertions.Expect(page.GetByText(pageTitle).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Unread" }).ClickAsync();
        await Assertions.Expect(page.GetByTitle("Unread").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Mark all read" }).ClickAsync();
        await Assertions.Expect(page.GetByText("No notifications yet."))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var shareResponseAfterRead = await request.PostAsync($"/api/content/pages/{pageId}/share/", new()
        {
            DataObject = new { userIds = new[] { user.UserId }, grantAccess = false }
        });
        Assert.True(shareResponseAfterRead.Ok, await shareResponseAfterRead.TextAsync());
        await page.ReloadAsync();
        await page.GetByText(pageTitle).First.ClickAsync();
        await page.WaitForURLAsync($"**/notes/{locator}", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = pageTitle, Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private static async Task<Guid> PostForIdAsync(IAPIRequestContext request, string path, object body)
    {
        var response = await request.PostAsync(path, new() { DataObject = body });
        Assert.True(response.Ok, await response.TextAsync());
        var json = await response.JsonAsync();
        return json!.Value.GetProperty("id").GetGuid();
    }
}
