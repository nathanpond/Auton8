namespace AutoNate.Web.Services.Agent.Providers;

// Wire-level abstraction over a single LLM provider for a specific
// connection. The resolver constructs one of these per request scope after
// loading the External Connection row and decrypting its api key. Provider
// implementations own all wire-format translation (Anthropic's
// `content_block_delta` events vs. OpenAI's `tool_calls[i].index`-keyed
// argument streaming) so callers stay in ChatStreamChunk space.
public interface IChatProvider
{
    // Provider-neutral kind discriminator: "Anthropic" or "OpenAI". Surfaced
    // for telemetry, audit, and "switch provider mid-conversation" guardrails.
    string Kind { get; }

    IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    // A 1-token ping the admin's "Test connection" button calls. Implementations
    // should use the cheapest model the connection can reach so admins don't
    // burn budget on every save.
    Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default);
}
