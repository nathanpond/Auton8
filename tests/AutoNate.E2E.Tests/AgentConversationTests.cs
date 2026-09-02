using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

public sealed class AgentConversationTests : E2ETestBase
{
    public AgentConversationTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Assistant_CrossPageSearchResizePersistenceAndDelete()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var title = TestNames.Prefixed("cross-page-chat");
        var create = await page.APIRequest.PostAsync("/api/agent/conversations", new()
        {
            DataObject = new { pageKey = "home", connectionId = (Guid?)null }
        });
        Assert.True(create.Ok, await create.TextAsync());
        var json = await create.JsonAsync();
        var conversationId = json!.Value.GetProperty("id").GetGuid();
        var rename = await page.APIRequest.PatchAsync($"/api/agent/conversations/{conversationId}", new()
        {
            DataObject = new { title }
        });
        Assert.True(rename.Ok, await rename.TextAsync());

        await page.GotoAsync("/query");
        await page.GetByLabel("Open Auton8 assistant").ClickAsync();
        await page.GetByPlaceholder("Search chats…").FillAsync(title);
        await page.GetByText(title, new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Loaded from", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var resize = page.GetByLabel("Resize chatbot");
        var before = await page.EvaluateAsync<string>("() => localStorage.getItem('autonate.agent.width') ?? ''");
        await resize.PressAsync("ArrowLeft");
        var after = await page.EvaluateAsync<string>("() => localStorage.getItem('autonate.agent.width') ?? ''");
        Assert.NotEqual(before, after);
        await page.ReloadAsync();
        Assert.Equal(after,
            await page.EvaluateAsync<string>("() => localStorage.getItem('autonate.agent.width') ?? ''"));

        await page.GetByPlaceholder("Search chats…").FillAsync(title);
        await page.GetByLabel($"Delete {title}").ClickAsync();

        // Confirmation is a Mantine modal, not window.confirm. The spec used
        // to register a page.Dialog handler, which never fires for a DOM
        // modal — so nothing was ever confirmed and the delete never ran. The
        // unconditional API DELETE further down (removed) hid that.
        var confirm = page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });
        await Assertions.Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await confirm.ClickAsync();
        // The UI click has to be what deleted it. This line used to issue an
        // unconditional API DELETE before the assertions below, so they passed
        // whether or not the button did anything — the only test of the delete
        // affordance could not fail (#84). Now it asserts the server already
        // has no such conversation, and deletes nothing itself.
        var afterDelete = await PollForDeletionAsync(page, conversationId);
        Assert.Equal(404, afterDelete);

        await page.ReloadAsync();
        await page.GetByPlaceholder("Search chats…").FillAsync(title);
        await Assertions.Expect(page.GetByText(title, new() { Exact = true }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // The click fires the DELETE and the confirm dialog resolves before the
    // request lands, so poll rather than assuming the first read is settled.
    private static async Task<int> PollForDeletionAsync(IPage page, Guid conversationId)
    {
        var status = 0;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var probe = await page.APIRequest.GetAsync($"/api/agent/conversations/{conversationId}");
            status = probe.Status;
            if (status == 404) return status;
            await page.WaitForTimeoutAsync(250);
        }
        return status;
    }
}
