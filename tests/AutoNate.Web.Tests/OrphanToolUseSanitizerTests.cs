using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Providers;
using Xunit;

namespace AutoNate.Web.Tests;

// Regression coverage for the Anthropic "tool_use without matching
// tool_result" failure mode. When the agent loop is cancelled between
// persisting the assistant turn (with tool_use blocks) and persisting
// the tool message (with tool_results), the next user turn loads a
// history shape Anthropic rejects with 400. `SanitizeOrphanToolUses`
// runs on every load to inject synthetic interrupted-result blocks
// in-memory only (not persisted) so the provider always sees
// well-formed history.
public sealed class OrphanToolUseSanitizerTests
{
    [Fact]
    public void InjectsResult_WhenAssistantToolUseHasNoFollowingToolMessage()
    {
        var history = new List<ChatMessage>
        {
            User("Add a paragraph to the doc."),
            Assistant("Sure, applying now.", ToolUse("toolu_orphan", "apply_page_action"))
        };
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        AgentSession.SanitizeOrphanToolUses(history, ids);

        // A synthetic Tool message must have been inserted right after
        // the assistant turn, with one ToolResultBlock for the orphan id.
        Assert.Equal(3, history.Count);
        Assert.Equal(ChatRole.Tool, history[2].Role);
        var block = Assert.Single(history[2].Blocks);
        var tr = Assert.IsType<ChatContentBlock.ToolResultBlock>(block);
        Assert.Equal("toolu_orphan", tr.ToolUseId);
        Assert.True(tr.IsError);
        // historyIds must grow in lock-step (sentinel Guid.Empty for
        // the synthetic message so downstream id-mapping skips it).
        Assert.Equal(3, ids.Count);
        Assert.Equal(Guid.Empty, ids[2]);
    }

    [Fact]
    public void InjectsResult_WhenAssistantToolUseIsFollowedByUserMessage()
    {
        // The orphan is the same case as above but with a user message
        // wedged in. That can happen if persistence raced with the user
        // sending a new turn before the tool_result row landed.
        var history = new List<ChatMessage>
        {
            Assistant("Working on it.", ToolUse("toolu_x", "apply_page_action")),
            User("Did that work?")
        };
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        AgentSession.SanitizeOrphanToolUses(history, ids);

        Assert.Equal(3, history.Count);
        Assert.Equal(ChatRole.Tool, history[1].Role);
        Assert.Equal(ChatRole.User, history[2].Role);
        var tr = Assert.IsType<ChatContentBlock.ToolResultBlock>(history[1].Blocks[0]);
        Assert.Equal("toolu_x", tr.ToolUseId);
    }

    [Fact]
    public void DoesNothing_WhenToolResultIsAlreadyPaired()
    {
        var history = new List<ChatMessage>
        {
            Assistant("Calling tool.", ToolUse("toolu_ok", "inspect_page")),
            Tool(ToolResult("toolu_ok", new { kind = "ok" }, isError: false))
        };
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var beforeCount = history.Count;

        AgentSession.SanitizeOrphanToolUses(history, ids);

        Assert.Equal(beforeCount, history.Count);
    }

    [Fact]
    public void AppendsToExistingToolMessage_WhenSomeButNotAllResultsArePaired()
    {
        // Edge case: assistant emitted two tool_use blocks in one turn,
        // and the persisted Tool message only carries one result (the
        // second call was cut off). We should append the missing
        // synthetic result to the existing Tool message, not insert a
        // new one — Anthropic doesn't allow two consecutive Tool
        // messages.
        var history = new List<ChatMessage>
        {
            Assistant("Running both.",
                ToolUse("toolu_a", "inspect_page"),
                ToolUse("toolu_b", "apply_page_action")),
            Tool(ToolResult("toolu_a", new { kind = "ok" }, isError: false))
        };
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        AgentSession.SanitizeOrphanToolUses(history, ids);

        Assert.Equal(2, history.Count); // No new message — appended.
        Assert.Equal(ChatRole.Tool, history[1].Role);
        Assert.Equal(2, history[1].Blocks.Count);
        var trA = Assert.IsType<ChatContentBlock.ToolResultBlock>(history[1].Blocks[0]);
        var trB = Assert.IsType<ChatContentBlock.ToolResultBlock>(history[1].Blocks[1]);
        Assert.Equal("toolu_a", trA.ToolUseId);
        Assert.False(trA.IsError);
        Assert.Equal("toolu_b", trB.ToolUseId);
        Assert.True(trB.IsError); // The synthetic one.
        // ids untouched because we appended rather than inserted.
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public void HealsMultipleSeparateOrphans()
    {
        // Two unrelated assistant turns, each with an orphan tool_use.
        // Both must be healed independently.
        var history = new List<ChatMessage>
        {
            User("Q1"),
            Assistant("A1", ToolUse("toolu_1", "inspect_page")),
            User("Q2"),
            Assistant("A2", ToolUse("toolu_2", "apply_page_action"))
        };
        var ids = new List<Guid>
        {
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
        };

        AgentSession.SanitizeOrphanToolUses(history, ids);

        Assert.Equal(6, history.Count);
        Assert.Equal(ChatRole.Tool, history[2].Role);
        Assert.Equal(ChatRole.Tool, history[5].Role);
        var tr1 = Assert.IsType<ChatContentBlock.ToolResultBlock>(history[2].Blocks[0]);
        var tr2 = Assert.IsType<ChatContentBlock.ToolResultBlock>(history[5].Blocks[0]);
        Assert.Equal("toolu_1", tr1.ToolUseId);
        Assert.Equal("toolu_2", tr2.ToolUseId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ChatMessage User(string text) =>
        new(ChatRole.User, new List<ChatContentBlock> { new ChatContentBlock.TextBlock(text) });

    private static ChatMessage Assistant(string text, params ChatContentBlock[] extra)
    {
        var blocks = new List<ChatContentBlock> { new ChatContentBlock.TextBlock(text) };
        blocks.AddRange(extra);
        return new ChatMessage(ChatRole.Assistant, blocks);
    }

    private static ChatMessage Tool(params ChatContentBlock[] blocks) =>
        new(ChatRole.Tool, blocks.ToList());

    private static ChatContentBlock.ToolUseBlock ToolUse(string id, string name) =>
        new(id, name, EmptyArgs());

    private static ChatContentBlock.ToolResultBlock ToolResult(string id, object payload, bool isError) =>
        new(id, JsonSerializer.SerializeToElement(payload), isError);

    private static JsonElement EmptyArgs()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }
}
