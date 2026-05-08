using System.Text;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class OpenAIChatProviderTests
{
    [Fact]
    public void BuildRequestBody_prepends_system_message_and_emits_tools_as_function_definitions()
    {
        var schema = ParseElement("""{"type":"object","properties":{"q":{"type":"string"}}}""");

        var request = new ChatRequest(
            Messages: new[]
            {
                new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("Hi") })
            },
            SystemPrompt: "You are a diagnostic assistant.",
            Tools: new[] { new ChatTool("search", "Search records", schema) },
            ModelId: "gpt-4.1");

        var body = OpenAIChatProvider.BuildRequestBody(request, "gpt-4.1", stream: true);

        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("You are a diagnostic assistant.", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());

        var tools = body["tools"]!.AsArray();
        var tool = Assert.Single(tools);
        Assert.Equal("function", tool!["type"]!.GetValue<string>());
        Assert.Equal("search", tool["function"]!["name"]!.GetValue<string>());
        Assert.Equal("object", tool["function"]!["parameters"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void BuildRequestBody_serializes_tool_use_blocks_as_assistant_tool_calls()
    {
        var args = ParseElement("""{"q":"hello"}""");
        var result = ParseElement("""{"hits":3}""");

        var request = new ChatRequest(
            Messages: new[]
            {
                new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("Search") }),
                new ChatMessage(ChatRole.Assistant, new ChatContentBlock[]
                {
                    new ChatContentBlock.ToolUseBlock("call_abc", "search", args)
                }),
                new ChatMessage(ChatRole.User, new ChatContentBlock[]
                {
                    new ChatContentBlock.ToolResultBlock("call_abc", result, IsError: false)
                })
            },
            SystemPrompt: null,
            Tools: Array.Empty<ChatTool>(),
            ModelId: "gpt-4.1");

        var body = OpenAIChatProvider.BuildRequestBody(request, "gpt-4.1", stream: false);
        var messages = body["messages"]!.AsArray();

        var assistant = messages.First(m => m!["role"]!.GetValue<string>() == "assistant")!;
        var toolCalls = assistant["tool_calls"]!.AsArray();
        var toolCall = Assert.Single(toolCalls);
        Assert.Equal("call_abc", toolCall!["id"]!.GetValue<string>());
        Assert.Equal("function", toolCall["type"]!.GetValue<string>());
        Assert.Equal("search", toolCall["function"]!["name"]!.GetValue<string>());

        var toolMsg = messages.First(m => m!["role"]!.GetValue<string>() == "tool")!;
        Assert.Equal("call_abc", toolMsg["tool_call_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task SseLineReader_decodes_openai_chat_completions_stream_with_text_and_tool_calls()
    {
        var sse = string.Join("\n",
            """data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"Hello"}}]}""",
            "",
            """data: {"choices":[{"index":0,"delta":{"content":" world"}}]}""",
            "",
            """data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_x","type":"function","function":{"name":"search","arguments":""}}]}}]}""",
            "",
            """data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q"}}]}}]}""",
            "",
            """data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\":\"hi\"}"}}]}}]}""",
            "",
            """data: {"choices":[{"index":0,"finish_reason":"tool_calls"}]}""",
            "",
            "data: [DONE]",
            "",
            ""
        );

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var frames = new List<SseFrame>();
        await foreach (var frame in SseLineReader.ReadFramesAsync(stream))
        {
            frames.Add(frame);
        }

        Assert.Contains(frames, f => f.Data.Contains("\"content\":\"Hello\""));
        Assert.Contains(frames, f => f.Data.Contains("call_x"));
        Assert.Contains(frames, f => f.Data.Contains("finish_reason"));
        Assert.Contains(frames, f => f.Data == "[DONE]");
    }

    private static JsonElement ParseElement(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
