using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoNate.Web.Services.Agent.Providers;

// OpenAI Chat Completions streaming over SSE. Wire format:
//
//   data: { "choices": [{"index":0, "delta":{"content":"Hi"}}] }
//   data: { "choices": [{"index":0, "delta":{"tool_calls":[{"index":0,"id":"...","function":{"name":"...","arguments":"{\"a"}}]}}] }
//   data: { "choices": [{"index":0, "delta":{"tool_calls":[{"index":0,"function":{"arguments":":1}"}}]}}] }
//   data: { "choices": [{"index":0, "finish_reason":"tool_calls"}] }
//   data: [DONE]
//
// Tool calls stream their JSON `arguments` as concatenated string fragments
// keyed by `tool_calls[i].index`. We buffer per-index, emit ToolUseStarted on
// first appearance with id+name, then ToolUseInputDelta per fragment, then
// ToolUseCompleted at finish_reason.
public sealed class OpenAIChatProvider : IChatProvider
{
    public string Kind => "OpenAI";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly Uri _baseUrl;

    public OpenAIChatProvider(HttpClient http, OpenAIProviderOptions options)
    {
        _http = http;
        _apiKey = options.ApiKey;
        _modelId = options.ModelId;
        _baseUrl = new Uri(options.BaseUrl ?? "https://api.openai.com");
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, _modelId, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "/v1/chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        httpRequest.Content = JsonContent.Create(body);

        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            yield return new ChatStreamChunk.Error(
                $"OpenAI returned {(int)response.StatusCode}: {Truncate(errorText, 1024)}",
                IsRetryable: (int)response.StatusCode >= 500);
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var toolBuffers = new Dictionary<int, ToolBuffer>();
        Usage? lastUsage = null;
        ChatStopReason? finalStop = null;

        await foreach (var frame in SseLineReader.ReadFramesAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(frame.Data)) continue;
            if (frame.Data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(frame.Data); }
            catch { continue; }
            if (node is null) continue;

            var usageNode = node["usage"];
            if (usageNode is not null)
            {
                lastUsage = new Usage(
                    InputTokens: usageNode["prompt_tokens"]?.GetValue<int>() ?? 0,
                    OutputTokens: usageNode["completion_tokens"]?.GetValue<int>() ?? 0,
                    CacheReadTokens: usageNode["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>(),
                    CacheWriteTokens: null);
            }

            var choices = node["choices"]?.AsArray();
            if (choices is null || choices.Count == 0) continue;

            var choice = choices[0];
            var delta = choice?["delta"];
            if (delta is not null)
            {
                var contentNode = delta["content"];
                if (contentNode is not null && contentNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String)
                {
                    var text = contentNode.GetValue<string>();
                    if (text.Length > 0) yield return new ChatStreamChunk.TextDelta(text);
                }

                var toolCalls = delta["tool_calls"]?.AsArray();
                if (toolCalls is not null)
                {
                    foreach (var tc in toolCalls)
                    {
                        var idx = tc?["index"]?.GetValue<int>() ?? -1;
                        if (idx < 0) continue;

                        if (!toolBuffers.TryGetValue(idx, out var buf))
                        {
                            buf = new ToolBuffer(string.Empty, string.Empty, new System.Text.StringBuilder());
                            toolBuffers[idx] = buf;
                        }

                        var idValue = tc?["id"]?.GetValue<string>();
                        var nameValue = tc?["function"]?["name"]?.GetValue<string>();
                        var startEmit = false;
                        if (!string.IsNullOrEmpty(idValue) && string.IsNullOrEmpty(buf.Id))
                        {
                            buf = buf with { Id = idValue };
                            toolBuffers[idx] = buf;
                            startEmit = true;
                        }
                        if (!string.IsNullOrEmpty(nameValue) && string.IsNullOrEmpty(buf.Name))
                        {
                            buf = buf with { Name = nameValue };
                            toolBuffers[idx] = buf;
                            startEmit = true;
                        }
                        if (startEmit && !string.IsNullOrEmpty(buf.Id) && !string.IsNullOrEmpty(buf.Name))
                        {
                            yield return new ChatStreamChunk.ToolUseStarted(buf.Id, buf.Name);
                        }

                        var argsFragment = tc?["function"]?["arguments"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(argsFragment))
                        {
                            buf.JsonBuffer.Append(argsFragment);
                            if (!string.IsNullOrEmpty(buf.Id))
                            {
                                yield return new ChatStreamChunk.ToolUseInputDelta(buf.Id, argsFragment);
                            }
                        }
                    }
                }
            }

            var finishReason = choice?["finish_reason"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(finishReason))
            {
                finalStop = MapFinishReason(finishReason);
                if (finishReason == "tool_calls")
                {
                    foreach (var (_, buf) in toolBuffers.OrderBy(kv => kv.Key))
                    {
                        var raw = buf.JsonBuffer.Length == 0 ? "{}" : buf.JsonBuffer.ToString();
                        JsonElement parsed;
                        try
                        {
                            using var doc = JsonDocument.Parse(raw);
                            parsed = doc.RootElement.Clone();
                        }
                        catch
                        {
                            using var doc = JsonDocument.Parse("{}");
                            parsed = doc.RootElement.Clone();
                        }
                        yield return new ChatStreamChunk.ToolUseCompleted(buf.Id, buf.Name, parsed);
                    }
                    toolBuffers.Clear();
                }
            }
        }

        if (finalStop is ChatStopReason stop)
        {
            yield return new ChatStreamChunk.MessageStop(stop, lastUsage);
        }
    }

    public async Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var body = BuildRequestBody(
                new ChatRequest(
                    Messages: new[]
                    {
                        new ChatMessage(
                            ChatRole.User,
                            new ChatContentBlock[] { new ChatContentBlock.TextBlock("ping") })
                    },
                    SystemPrompt: null,
                    Tools: Array.Empty<ChatTool>(),
                    ModelId: _modelId,
                    MaxTokens: 8),
                _modelId,
                stream: false);

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "/v1/chat/completions"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            req.Content = JsonContent.Create(body);

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new ChatProviderTestResult(false, sw.ElapsedMilliseconds, null, $"{(int)resp.StatusCode}: {Truncate(text, 256)}");
            }
            return new ChatProviderTestResult(true, sw.ElapsedMilliseconds, _modelId, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ChatProviderTestResult(false, sw.ElapsedMilliseconds, null, ex.Message);
        }
    }

    public static JsonObject BuildRequestBody(ChatRequest request, string modelId, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = modelId,
            ["max_tokens"] = request.MaxTokens ?? 4096
        };
        if (request.Temperature is double t) body["temperature"] = t;
        if (stream) body["stream"] = true;

        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        }
        foreach (var msg in request.Messages)
        {
            if (msg.Role == ChatRole.System)
            {
                // Already prepended above.
                continue;
            }

            // Walk blocks. Text blocks compose into a single content string;
            // tool_use blocks become a tool_calls array on an assistant
            // message; tool_result blocks become individual tool messages.
            var textBuf = new System.Text.StringBuilder();
            var toolUses = new JsonArray();
            var toolResults = new List<(string ToolCallId, JsonElement Result)>();
            foreach (var block in msg.Blocks)
            {
                switch (block)
                {
                    case ChatContentBlock.TextBlock t2:
                        if (textBuf.Length > 0) textBuf.Append('\n');
                        textBuf.Append(t2.Text);
                        break;
                    case ChatContentBlock.ToolUseBlock tu:
                        toolUses.Add(new JsonObject
                        {
                            ["id"] = tu.ToolUseId,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = tu.Name,
                                ["arguments"] = tu.Args.GetRawText()
                            }
                        });
                        break;
                    case ChatContentBlock.ToolResultBlock tr:
                        toolResults.Add((tr.ToolUseId, tr.Result));
                        break;
                }
            }

            if (msg.Role == ChatRole.Assistant)
            {
                var assistant = new JsonObject { ["role"] = "assistant" };
                if (textBuf.Length > 0) assistant["content"] = textBuf.ToString();
                if (toolUses.Count > 0) assistant["tool_calls"] = toolUses;
                if (assistant.ContainsKey("content") || toolUses.Count > 0)
                {
                    messages.Add(assistant);
                }
            }
            else if (msg.Role == ChatRole.User)
            {
                if (textBuf.Length > 0)
                {
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = textBuf.ToString() });
                }
                foreach (var (id, result) in toolResults)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = id,
                        ["content"] = result.GetRawText()
                    });
                }
            }
            else if (msg.Role == ChatRole.Tool)
            {
                foreach (var (id, result) in toolResults)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = id,
                        ["content"] = result.GetRawText()
                    });
                }
            }
        }

        body["messages"] = messages;

        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText())
                    }
                });
            }
            body["tools"] = tools;
        }

        return body;
    }

    private static ChatStopReason MapFinishReason(string raw) => raw switch
    {
        "stop" => ChatStopReason.EndTurn,
        "tool_calls" => ChatStopReason.ToolUse,
        "length" => ChatStopReason.MaxTokens,
        "function_call" => ChatStopReason.ToolUse,
        _ => ChatStopReason.EndTurn
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private sealed record class ToolBuffer(string Id, string Name, System.Text.StringBuilder JsonBuffer);
}

public sealed record class OpenAIProviderOptions(string ApiKey, string ModelId, string? BaseUrl);
