using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Providers;
using Xunit;

namespace AutoNate.Web.Tests;

// Coverage for AgentSession.ElideOversizedHistoryBlobs — the replay-cost
// guardrail that drops oversized tool_use args / tool_result content from
// prior turns before the conversation history is shipped to the provider.
// Persistence is untouched; only the in-memory replay copy is altered.
public sealed class OversizedBlobElisionTests
{
    [Fact]
    public void LeavesSmallToolResultsUntouched()
    {
        // A typical inspect_page snapshot is well under the threshold —
        // it should pass through bit-for-bit so the model keeps seeing
        // page state across turns.
        var smallResult = JsonSerializer.SerializeToElement(new
        {
            kind = "inspect_page_result",
            summary = "Open document",
            dataKeys = new[] { "title", "body" }
        });
        var history = new List<ChatMessage>
        {
            Assistant(ToolUse("toolu_1", "inspect_page", JsonSerializer.SerializeToElement(new { })) ),
            Tool(ToolResult("toolu_1", smallResult, isError: false))
        };

        AgentSession.ElideOversizedHistoryBlobs(history);

        var tr = (ChatContentBlock.ToolResultBlock)history[1].Blocks[0];
        Assert.Equal(smallResult.GetRawText(), tr.Result.GetRawText());
    }

    [Fact]
    public void ElidesOversizedToolResults()
    {
        var bigText = new string('A', 50 * 1024);
        var bigResult = JsonSerializer.SerializeToElement(new
        {
            kind = "web_fetch_result",
            data = new { text = bigText, status = 200 }
        });
        var history = new List<ChatMessage>
        {
            Assistant(ToolUse("toolu_fetch", "fetch_url", JsonSerializer.SerializeToElement(new { url = "https://example.com" }))),
            Tool(ToolResult("toolu_fetch", bigResult, isError: false))
        };

        AgentSession.ElideOversizedHistoryBlobs(history);

        var tr = (ChatContentBlock.ToolResultBlock)history[1].Blocks[0];
        // Pairing fields preserved so Anthropic's tool_use/tool_result
        // matching doesn't break.
        Assert.Equal("toolu_fetch", tr.ToolUseId);
        Assert.False(tr.IsError);
        // Stub carries the _elided flag + originalKind + originalSize
        // so the model can reason about what was dropped.
        var data = tr.Result;
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.True(data.GetProperty("_elided").GetBoolean());
        Assert.Equal("web_fetch_result", data.GetProperty("_originalKind").GetString());
        Assert.True(data.GetProperty("_originalSizeBytes").GetInt32() > 40 * 1024);
        // And the stub itself is much smaller than the original — the
        // whole point of elision.
        Assert.True(data.GetRawText().Length < 500);
    }

    [Fact]
    public void ElidesOversizedToolUseArgs()
    {
        // apply_page_action with a giant markdown payload — a real-world
        // case from the editor chat panel. We want the args replaced with
        // a stub so subsequent replays don't carry the markdown blob.
        var bigMarkdown = new string('M', 8 * 1024);
        var bigArgs = JsonSerializer.SerializeToElement(new
        {
            action = "append_markdown",
            args = new { markdown = bigMarkdown },
            confirmed = true
        });
        var history = new List<ChatMessage>
        {
            Assistant(ToolUse("toolu_apply", "apply_page_action", bigArgs)),
            Tool(ToolResult("toolu_apply",
                JsonSerializer.SerializeToElement(new { kind = "page_action_applied", data = new { summary = "Appended 3 blocks." } }),
                isError: false))
        };

        AgentSession.ElideOversizedHistoryBlobs(history);

        var tu = (ChatContentBlock.ToolUseBlock)history[0].Blocks[0];
        // Name + id preserved.
        Assert.Equal("apply_page_action", tu.Name);
        Assert.Equal("toolu_apply", tu.ToolUseId);
        // Args replaced with a stub.
        Assert.True(tu.Args.GetProperty("_elided").GetBoolean());
        Assert.True(tu.Args.GetProperty("_originalSizeBytes").GetInt32() > 7 * 1024);
        // And the matching tool_result wasn't touched — it's small.
        var tr = (ChatContentBlock.ToolResultBlock)history[1].Blocks[0];
        Assert.Equal("page_action_applied",
            tr.Result.GetProperty("kind").GetString());
    }

    [Fact]
    public void MixedHistoryElidesOnlyTheOversizedPieces()
    {
        // Realistic multi-turn replay: small inspect_page, big web_fetch,
        // small apply_page_action. Only the web_fetch result should be
        // touched; everything else passes through.
        var smallInspect = JsonSerializer.SerializeToElement(new
        {
            kind = "inspect_page_result",
            summary = "ok"
        });
        var bigFetch = JsonSerializer.SerializeToElement(new
        {
            kind = "web_fetch_result",
            data = new { text = new string('A', 50 * 1024) }
        });
        var smallApply = JsonSerializer.SerializeToElement(new
        {
            kind = "page_action_applied",
            data = new { summary = "Appended 1 paragraph." }
        });
        var history = new List<ChatMessage>
        {
            Assistant(ToolUse("u1", "inspect_page", EmptyArgs())),
            Tool(ToolResult("u1", smallInspect, false)),
            Assistant(ToolUse("u2", "fetch_url", JsonSerializer.SerializeToElement(new { url = "https://example.com" }))),
            Tool(ToolResult("u2", bigFetch, false)),
            Assistant(ToolUse("u3", "apply_page_action",
                JsonSerializer.SerializeToElement(new { action = "append_markdown", args = new { markdown = "short" }, confirmed = true }))),
            Tool(ToolResult("u3", smallApply, false))
        };

        AgentSession.ElideOversizedHistoryBlobs(history);

        var inspectRes = (ChatContentBlock.ToolResultBlock)history[1].Blocks[0];
        var fetchRes = (ChatContentBlock.ToolResultBlock)history[3].Blocks[0];
        var applyRes = (ChatContentBlock.ToolResultBlock)history[5].Blocks[0];

        Assert.Equal("inspect_page_result", inspectRes.Result.GetProperty("kind").GetString());
        Assert.True(fetchRes.Result.GetProperty("_elided").GetBoolean());
        Assert.Equal("page_action_applied", applyRes.Result.GetProperty("kind").GetString());
    }

    [Fact]
    public void PreservesIsErrorFlagOnElidedResults()
    {
        // An oversized tool_result that's also an error (e.g., a large
        // stack trace from a failed search) needs to keep IsError=true
        // so the model still treats it as a failure on replay.
        var bigErrorResult = JsonSerializer.SerializeToElement(new
        {
            kind = "error",
            message = new string('E', 10 * 1024)
        });
        var history = new List<ChatMessage>
        {
            Assistant(ToolUse("u1", "search", EmptyArgs())),
            Tool(ToolResult("u1", bigErrorResult, isError: true))
        };

        AgentSession.ElideOversizedHistoryBlobs(history);

        var tr = (ChatContentBlock.ToolResultBlock)history[1].Blocks[0];
        Assert.True(tr.IsError);
        Assert.True(tr.Result.GetProperty("_elided").GetBoolean());
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ChatMessage Assistant(params ChatContentBlock[] blocks) =>
        new(ChatRole.Assistant, blocks.ToList());

    private static ChatMessage Tool(params ChatContentBlock[] blocks) =>
        new(ChatRole.Tool, blocks.ToList());

    private static ChatContentBlock.ToolUseBlock ToolUse(string id, string name, JsonElement args) =>
        new(id, name, args);

    private static ChatContentBlock.ToolResultBlock ToolResult(string id, JsonElement result, bool isError) =>
        new(id, result, isError);

    private static JsonElement EmptyArgs()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }
}
