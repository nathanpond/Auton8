using AutoNate.Web.Services.Agent.Conversations;
using AutoNate.Web.Services.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AgentConversationSummaryStoreTests
{
    private static readonly Guid TestUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task LoadMessagesWithIdsAsync_replays_a_summary_in_place_of_subsumed_prefix()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Force host startup so the schema initializer runs.
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentConversationStore>();

        var conversation = await store.CreateAsync(TestUserId, pageKey: "test", connectionId: null, providerKind: null, modelId: null);

        // Three early turns — these should be subsumed by the summary.
        await store.AppendMessageAsync(conversation.Id, ChatRole.User,
            new ChatContentBlock[] { new ChatContentBlock.TextBlock("first question") },
            providerKind: null, modelId: null, usage: null, stopReason: null);
        await store.AppendMessageAsync(conversation.Id, ChatRole.Assistant,
            new ChatContentBlock[] { new ChatContentBlock.TextBlock("first answer") },
            providerKind: "Anthropic", modelId: "claude-sonnet-4-6", usage: null, stopReason: null);
        var u2 = await store.AppendMessageAsync(conversation.Id, ChatRole.User,
            new ChatContentBlock[] { new ChatContentBlock.TextBlock("second question") },
            providerKind: null, modelId: null, usage: null, stopReason: null);

        // Summary subsumes u1..u2.
        var summaryId = await store.AppendSummaryAsync(
            conversation.Id,
            "User asked first and second questions; assistant answered both.",
            replacesThroughMessageId: u2,
            providerKind: "Anthropic",
            modelId: "claude-sonnet-4-6",
            usage: new Usage(10, 20, null, null));

        // Two later turns — these survive verbatim after the summary.
        var a2 = await store.AppendMessageAsync(conversation.Id, ChatRole.Assistant,
            new ChatContentBlock[] { new ChatContentBlock.TextBlock("third turn") },
            providerKind: "Anthropic", modelId: "claude-sonnet-4-6", usage: null, stopReason: null);
        var u3 = await store.AppendMessageAsync(conversation.Id, ChatRole.User,
            new ChatContentBlock[] { new ChatContentBlock.TextBlock("fourth question") },
            providerKind: null, modelId: null, usage: null, stopReason: null);

        var loaded = await store.LoadMessagesWithIdsAsync(conversation.Id);

        // Expected playback: synthetic summary turn (assistant) + a2 + u3.
        // u1, a1, u2 must all be elided.
        Assert.Equal(3, loaded.Count);

        Assert.Equal(summaryId, loaded[0].Id);
        Assert.Equal(ChatRole.Assistant, loaded[0].Message.Role);
        var summaryText = (loaded[0].Message.Blocks[0] as ChatContentBlock.TextBlock)?.Text;
        Assert.Equal("User asked first and second questions; assistant answered both.", summaryText);

        Assert.Equal(a2, loaded[1].Id);
        Assert.Equal(ChatRole.Assistant, loaded[1].Message.Role);
        Assert.Equal("third turn", (loaded[1].Message.Blocks[0] as ChatContentBlock.TextBlock)?.Text);

        Assert.Equal(u3, loaded[2].Id);
        Assert.Equal(ChatRole.User, loaded[2].Message.Role);
        Assert.Equal("fourth question", (loaded[2].Message.Blocks[0] as ChatContentBlock.TextBlock)?.Text);

        // The raw transcript (admin/audit view) keeps every row including
        // the subsumed prefix and the summary itself, so the audit trail
        // never loses the original messages.
        var detail = await store.GetForUserAsync(conversation.Id, TestUserId);
        Assert.NotNull(detail);
        Assert.Equal(6, detail!.Messages.Count);
    }
}
