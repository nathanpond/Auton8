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
        await page.GetByLabel("Open AutoNate assistant").ClickAsync();
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
        Task? acceptDialogTask = null;
        page.Dialog += (_, browserDialog) => acceptDialogTask = browserDialog.AcceptAsync();
        await page.GetByLabel($"Delete {title}").ClickAsync();
        if (acceptDialogTask is not null) await acceptDialogTask;
        await page.WaitForTimeoutAsync(250);
        var cleanup = await page.APIRequest.DeleteAsync($"/api/agent/conversations/{conversationId}");
        Assert.True(cleanup.Ok || cleanup.Status == 404, await cleanup.TextAsync());
        await page.ReloadAsync();
        await page.GetByPlaceholder("Search chats…").FillAsync(title);
        await Assertions.Expect(page.GetByText(title, new() { Exact = true }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
