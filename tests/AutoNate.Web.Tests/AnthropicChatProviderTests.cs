using System.Text;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class AnthropicChatProviderTests
{
    [Fact]
    public void BuildRequestBody_promotes_system_prompt_to_top_level_field()
    {
        var request = new ChatRequest(
            Messages: new[]
            {
                new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("Hi") })
            },
            SystemPrompt: "You are a diagnostic assistant.",
            Tools: Array.Empty<ChatTool>(),
            ModelId: "claude-sonnet-4-6");

        var body = AnthropicChatProvider.BuildRequestBody(request, "claude-sonnet-4-6", stream: true);

        Assert.Equal("claude-sonnet-4-6", body["model"]!.GetValue<string>());
        Assert.Equal("You are a diagnostic assistant.", body["system"]!.GetValue<string>());
        Assert.True(body["stream"]!.GetValue<bool>());

        var messages = body["messages"]!.AsArray();
        var only = Assert.Single(messages);
        Assert.Equal("user", only!["role"]!.GetValue<string>());
    }

    [Fact]
    public void BuildRequestBody_serializes_tools_with_input_schema()
    {
        var schema = ParseElement("""{"type":"object","properties":{"q":{"type":"string"}},"required":["q"]}""");
        var request = new ChatRequest(
            Messages: new[]
            {
                new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("ping") })
            },
            SystemPrompt: null,
            Tools: new[] { new ChatTool("search", "Search the records", schema) },
            ModelId: "claude-sonnet-4-6");

        var body = AnthropicChatProvider.BuildRequestBody(request, "claude-sonnet-4-6", stream: false);

        var tools = body["tools"]!.AsArray();
        var tool = Assert.Single(tools);
        Assert.Equal("search", tool!["name"]!.GetValue<string>());
        Assert.Equal("Search the records", tool["description"]!.GetValue<string>());
        Assert.Equal("object", tool["input_schema"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void BuildRequestBody_serializes_tool_use_and_tool_result_blocks_per_anthropic_protocol()
    {
        var args = ParseElement("""{"q":"hello"}""");
        var result = ParseElement("""{"hits":3}""");

        var request = new ChatRequest(
            Messages: new[]
            {
                new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("Search") }),
                new ChatMessage(ChatRole.Assistant, new ChatContentBlock[]
                {
                    new ChatContentBlock.TextBlock("I'll search."),
                    new ChatContentBlock.ToolUseBlock("toolu_abc", "search", args)
                }),
                new ChatMessage(ChatRole.User, new ChatContentBlock[]
                {
                    new ChatContentBlock.ToolResultBlock("toolu_abc", result, IsError: false)
                })
            },
            SystemPrompt: null,
            Tools: Array.Empty<ChatTool>(),
            ModelId: "claude-sonnet-4-6");

        var body = AnthropicChatProvider.BuildRequestBody(request, "claude-sonnet-4-6", stream: false);
        var messages = body["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);

        var assistant = messages[1]!;
        Assert.Equal("assistant", assistant["role"]!.GetValue<string>());
        var content = assistant["content"]!.AsArray();
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("tool_use", content[1]!["type"]!.GetValue<string>());
        Assert.Equal("toolu_abc", content[1]!["id"]!.GetValue<string>());
        Assert.Equal("search", content[1]!["name"]!.GetValue<string>());

        var toolResult = messages[2]!;
        Assert.Equal("user", toolResult["role"]!.GetValue<string>());
        var trContent = toolResult["content"]!.AsArray();
        Assert.Equal("tool_result", trContent[0]!["type"]!.GetValue<string>());
        Assert.Equal("toolu_abc", trContent[0]!["tool_use_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task SseLineReader_decodes_text_and_tool_use_chunks_from_anthropic_stream()
    {
        // Captured-style SSE bytes representing: text deltas → tool_use → end_turn.
        var sse = string.Join("\n",
            "event: content_block_start",
            """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            "",
            "event: content_block_delta",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""",
            "",
            "event: content_block_delta",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}""",
            "",
            "event: content_block_stop",
            """data: {"type":"content_block_stop","index":0}""",
            "",
            "event: content_block_start",
            """data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_x","name":"search","input":{}}}""",
            "",
            "event: content_block_delta",
            """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"q\":"}}""",
            "",
            "event: content_block_delta",
            """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"hi\"}"}}""",
            "",
            "event: content_block_stop",
            """data: {"type":"content_block_stop","index":1}""",
            "",
            "event: message_delta",
            """data: {"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"input_tokens":10,"output_tokens":7}}""",
            "",
            ""
        );

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var frames = new List<SseFrame>();
        await foreach (var frame in SseLineReader.ReadFramesAsync(stream))
        {
            frames.Add(frame);
        }

        // Sanity: we got the expected event names in order.
        Assert.Contains(frames, f => f.Event == "content_block_start" && f.Data.Contains("\"text\""));
        Assert.Contains(frames, f => f.Event == "content_block_delta" && f.Data.Contains("text_delta"));
        Assert.Contains(frames, f => f.Event == "content_block_start" && f.Data.Contains("\"tool_use\""));
        Assert.Contains(frames, f => f.Event == "message_delta" && f.Data.Contains("tool_use"));
    }

    private static JsonElement ParseElement(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
