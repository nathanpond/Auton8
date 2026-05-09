using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Providers;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class ConversationHistoryTrimmerTests
{
    private static ChatMessage Text(ChatRole role, string text) =>
        new(role, new ChatContentBlock[] { new ChatContentBlock.TextBlock(text) });

    private static ChatMessage AssistantWithToolUse(string toolUseId, string name, string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        return new ChatMessage(ChatRole.Assistant, new ChatContentBlock[]
        {
            new ChatContentBlock.ToolUseBlock(toolUseId, name, doc.RootElement.Clone())
        });
    }

    private static ChatMessage ToolResult(string toolUseId, string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        return new ChatMessage(ChatRole.Tool, new ChatContentBlock[]
        {
            new ChatContentBlock.ToolResultBlock(toolUseId, doc.RootElement.Clone(), IsError: false)
        });
    }

    [Fact]
    public void Returns_history_unchanged_when_under_budget()
    {
        var history = new[]
        {
            Text(ChatRole.User, "hello"),
            Text(ChatRole.Assistant, "hi"),
            Text(ChatRole.User, "and now what?")
        };

        var result = ConversationHistoryTrimmer.Trim(
            history,
            systemPrompt: "you are helpful",
            tools: Array.Empty<ChatTool>(),
            contextWindowTokens: 200_000,
            maxOutputTokens: 4096);

        Assert.Equal(0, result.DroppedCount);
        Assert.Same(history, result.Messages);
    }

    [Fact]
    public void Trims_oldest_messages_when_over_budget()
    {
        // 100KB of text per assistant message → ~33K estimated tokens each.
        // With a 50K context window the trimmer must drop the older turns.
        var bigText = new string('a', 100_000);
        var history = new[]
        {
            Text(ChatRole.User, "first question"),
            Text(ChatRole.Assistant, bigText),
            Text(ChatRole.User, "second question"),
            Text(ChatRole.Assistant, bigText),
            Text(ChatRole.User, "active question")
        };

        var result = ConversationHistoryTrimmer.Trim(
            history,
            systemPrompt: null,
            tools: Array.Empty<ChatTool>(),
            contextWindowTokens: 50_000,
            maxOutputTokens: 4096);

        Assert.True(result.DroppedCount > 0, "expected at least one message to be dropped");
        // The active turn (last user message) is always preserved.
        Assert.Contains(result.Messages, m =>
            m.Role == ChatRole.User
            && m.Blocks[0] is ChatContentBlock.TextBlock t
            && t.Text == "active question");
        // After trimming, the head must be a user message — never assistant
        // or tool, which would otherwise leave a tool_use orphaned.
        Assert.Equal(ChatRole.User, result.Messages[0].Role);
    }

    [Fact]
    public void Never_strands_a_tool_result_at_the_head()
    {
        // Sequence: user → assistant(tool_use) → tool(tool_result) → user(active).
        // If we naively trim from the front by message count we could leave
        // tool(result) at the head, which providers reject. The trimmer
        // must keep advancing past non-User roles.
        var bigText = new string('a', 100_000);
        var history = new[]
        {
            Text(ChatRole.User, "first question " + bigText),
            AssistantWithToolUse("t1", "lookup", "{\"q\":\"x\"}"),
            ToolResult("t1", "{\"answer\":\"y\"}"),
            Text(ChatRole.User, "active question")
        };

        var result = ConversationHistoryTrimmer.Trim(
            history,
            systemPrompt: null,
            tools: Array.Empty<ChatTool>(),
            contextWindowTokens: 20_000,
            maxOutputTokens: 4096);

        Assert.True(result.DroppedCount >= 1);
        Assert.Equal(ChatRole.User, result.Messages[0].Role);
    }

    [Fact]
    public void Returns_history_unchanged_when_active_turn_alone_blows_budget()
    {
        // Pathological: a single user message bigger than the whole window.
        // The trimmer can't help — it must return the history as-is so the
        // provider's error surfaces (we don't silently truncate the active
        // turn).
        var huge = new string('a', 1_000_000);
        var history = new[] { Text(ChatRole.User, huge) };

        var result = ConversationHistoryTrimmer.Trim(
            history,
            systemPrompt: null,
            tools: Array.Empty<ChatTool>(),
            contextWindowTokens: 50_000,
            maxOutputTokens: 4096);

        Assert.Equal(0, result.DroppedCount);
        Assert.Same(history, result.Messages);
    }
}
