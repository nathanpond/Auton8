using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoNate.Web.Services.Agent.Providers;

// Anthropic Messages API streaming over SSE. Wire format:
//
//   event: message_start         { "message": { ... } }
//   event: content_block_start   { "index": 0, "content_block": {"type":"text"|"tool_use", ...} }
//   event: content_block_delta   { "index": 0, "delta": {"type":"text_delta"|"input_json_delta", ...} }
//   event: content_block_stop    { "index": 0 }
//   event: message_delta         { "delta": {"stop_reason": "..."}, "usage": {...} }
//   event: message_stop          {}
//
// Tool use: the model emits a `tool_use` content block whose input streams as
// `input_json_delta` partials; we accumulate them by index, then emit a
// ToolUseCompleted on content_block_stop for that index.
public sealed class AnthropicChatProvider : IChatProvider
{
    public string Kind => "Anthropic";

    public string ModelId => _modelId;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly Uri _baseUrl;

    public AnthropicChatProvider(HttpClient http, AnthropicProviderOptions options)
    {
        _http = http;
        _apiKey = options.ApiKey;
        _modelId = options.ModelId;
        _baseUrl = new Uri(options.BaseUrl ?? "https://api.anthropic.com");
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, _modelId, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "/v1/messages"));
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
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
                $"Anthropic returned {(int)response.StatusCode}: {Truncate(errorText, 1024)}",
                IsRetryable: (int)response.StatusCode >= 500);
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // Per content_block index, the type and accumulated input JSON for
        // tool_use blocks. Only used for tool_use blocks; text blocks emit
        // deltas immediately and don't need buffering here.
        var toolBuffers = new Dictionary<int, ToolBuffer>();

        await foreach (var frame in SseLineReader.ReadFramesAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(frame.Data)) continue;
            JsonNode? node;
            try { node = JsonNode.Parse(frame.Data); }
            catch { continue; }
            if (node is null) continue;

            switch (frame.Event)
            {
                case "content_block_start":
                {
                    var idx = node["index"]?.GetValue<int>() ?? -1;
                    var block = node["content_block"];
                    var type = block?["type"]?.GetValue<string>();
                    if (type == "tool_use")
                    {
                        var id = block?["id"]?.GetValue<string>() ?? string.Empty;
                        var name = block?["name"]?.GetValue<string>() ?? string.Empty;
                        toolBuffers[idx] = new ToolBuffer(id, name, new System.Text.StringBuilder());
                        yield return new ChatStreamChunk.ToolUseStarted(id, name);
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var idx = node["index"]?.GetValue<int>() ?? -1;
                    var delta = node["delta"];
                    var dtype = delta?["type"]?.GetValue<string>();
                    if (dtype == "text_delta")
                    {
                        var text = delta?["text"]?.GetValue<string>() ?? string.Empty;
                        if (text.Length > 0) yield return new ChatStreamChunk.TextDelta(text);
                    }
                    else if (dtype == "input_json_delta" && toolBuffers.TryGetValue(idx, out var buf))
                    {
                        var partial = delta?["partial_json"]?.GetValue<string>() ?? string.Empty;
                        buf.JsonBuffer.Append(partial);
                        yield return new ChatStreamChunk.ToolUseInputDelta(buf.Id, partial);
                    }
                    break;
                }
                case "content_block_stop":
                {
                    var idx = node["index"]?.GetValue<int>() ?? -1;
                    if (toolBuffers.TryGetValue(idx, out var buf))
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
                        toolBuffers.Remove(idx);
                    }
                    break;
                }
                case "message_delta":
                {
                    var stopRaw = node["delta"]?["stop_reason"]?.GetValue<string>();
                    if (stopRaw is not null)
                    {
                        var usage = ParseUsage(node["usage"]);
                        yield return new ChatStreamChunk.MessageStop(MapStopReason(stopRaw), usage);
                    }
                    break;
                }
                case "error":
                {
                    var msg = node["error"]?["message"]?.GetValue<string>() ?? "Anthropic error";
                    yield return new ChatStreamChunk.Error(msg, IsRetryable: false);
                    yield break;
                }
            }
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

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, "/v1/messages"));
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
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

    // Public-static so the test project can assert request shape without
    // calling SendAsync.
    public static JsonObject BuildRequestBody(ChatRequest request, string modelId, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = modelId,
            ["max_tokens"] = request.MaxTokens ?? 4096
        };
        if (request.Temperature is double t) body["temperature"] = t;
        if (stream) body["stream"] = true;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            body["system"] = request.SystemPrompt;
        }
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.JsonSchema.GetRawText())
                });
            }
            body["tools"] = tools;
        }

        var messages = new JsonArray();
        foreach (var msg in request.Messages)
        {
            // Anthropic doesn't accept "system" role in messages; the system
            // prompt above carries it. Skip system messages here defensively.
            if (msg.Role == ChatRole.System) continue;

            var role = msg.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => "user", // tool_results go inside a user message
                _ => "user"
            };

            var content = new JsonArray();
            foreach (var block in msg.Blocks)
            {
                switch (block)
                {
                    case ChatContentBlock.TextBlock t2:
                        content.Add(new JsonObject { ["type"] = "text", ["text"] = t2.Text });
                        break;
                    case ChatContentBlock.ToolUseBlock tu:
                        content.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = tu.ToolUseId,
                            ["name"] = tu.Name,
                            ["input"] = JsonNode.Parse(tu.Args.GetRawText())
                        });
                        break;
                    case ChatContentBlock.ToolResultBlock tr:
                        var resultObj = new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = tr.ToolUseId,
                            ["content"] = tr.Result.GetRawText()
                        };
                        if (tr.IsError) resultObj["is_error"] = true;
                        content.Add(resultObj);
                        break;
                }
            }

            messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }
        body["messages"] = messages;
        return body;
    }

    private static Usage? ParseUsage(JsonNode? node)
    {
        if (node is null) return null;
        return new Usage(
            InputTokens: node["input_tokens"]?.GetValue<int>() ?? 0,
            OutputTokens: node["output_tokens"]?.GetValue<int>() ?? 0,
            CacheReadTokens: node["cache_read_input_tokens"]?.GetValue<int>(),
            CacheWriteTokens: node["cache_creation_input_tokens"]?.GetValue<int>());
    }

    private static ChatStopReason MapStopReason(string raw) => raw switch
    {
        "end_turn" => ChatStopReason.EndTurn,
        "tool_use" => ChatStopReason.ToolUse,
        "max_tokens" => ChatStopReason.MaxTokens,
        "stop_sequence" => ChatStopReason.StopSequence,
        _ => ChatStopReason.EndTurn
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private sealed record class ToolBuffer(string Id, string Name, System.Text.StringBuilder JsonBuffer);
}

public sealed record class AnthropicProviderOptions(string ApiKey, string ModelId, string? BaseUrl);
