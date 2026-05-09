using AutoNate.Web.Services.Agent.Providers;

namespace AutoNate.Web.Services.Agent.Loop;

// Sliding-window trim of in-flight conversation history so a request stays
// inside the provider's context window. The persisted conversation is
// untouched — this only narrows what we hand to the LLM for the next turn.
//
// We don't ship a real tokenizer (the provider tokenizers aren't shared
// across vendors), so the estimate is character-based with a deliberately
// pessimistic divisor. That lets us trim early rather than late, which is
// the right call: the cost of trimming an extra message is small, the cost
// of a 400 "prompt is too long" mid-conversation is the user has to retry.
//
// Trimming preserves these invariants:
//   1. The most recent user message and everything after it stays. That's
//      the active turn we're trying to send.
//   2. After trimming, the first surviving message is always ChatRole.User —
//      we never strand an assistant tool_use without its preceding context,
//      and we never start with an orphaned tool_result.
public static class ConversationHistoryTrimmer
{
    // English averages ~3.5-4 chars/token but JSON / code / dense tool
    // output runs much hotter — sometimes under 2 chars/token. We had this
    // at 3.0 and shipped a turn that came in 1,366 tokens past Anthropic's
    // 200K cap, so we're using 2.5 now: still right for English, conservative
    // for JSON, and the cost of over-trimming is just a slightly shorter
    // context window.
    private const double CharsPerTokenEstimate = 2.5;

    // Slack reserved on top of MaxOutputTokens for stop tokens, system
    // overhead, and tokenizer drift (we're estimating, not counting). 8K
    // covers the kind of estimator drift we hit in production on a 200K
    // window with JSON-heavy tool results.
    public const int SafetyMarginTokens = 8_000;

    public sealed record class TrimResult(
        IReadOnlyList<ChatMessage> Messages,
        int DroppedCount,
        int EstimatedInputTokens,
        int EffectiveBudgetTokens);

    public static TrimResult Trim(
        IReadOnlyList<ChatMessage> history,
        string? systemPrompt,
        IReadOnlyList<ChatTool> tools,
        int contextWindowTokens,
        int maxOutputTokens)
    {
        var fixedOverhead = EstimateFixedOverhead(systemPrompt, tools);
        var budget = Math.Max(1024, contextWindowTokens - maxOutputTokens - SafetyMarginTokens - fixedOverhead);

        // Estimate per-message cost once up front. ChatMessage is immutable,
        // so the indices stay stable as we drop from the head.
        var messageTokens = new int[history.Count];
        var totalTokens = 0;
        for (var i = 0; i < history.Count; i++)
        {
            messageTokens[i] = EstimateMessageTokens(history[i]);
            totalTokens += messageTokens[i];
        }

        if (totalTokens <= budget)
        {
            return new TrimResult(history, 0, totalTokens + fixedOverhead, budget + fixedOverhead);
        }

        // Scan from the end to find the boundary of the active turn — the
        // last user message and everything after it. Those stay.
        var protectedStart = history.Count;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == ChatRole.User)
            {
                protectedStart = i;
                break;
            }
        }

        // Drop oldest messages, but only from the prefix [0, protectedStart).
        // After each drop, advance to the next message whose role is User so
        // we never leave the head dangling on an assistant or tool message.
        var dropTo = 0;
        while (dropTo < protectedStart)
        {
            if (totalTokens <= budget && history[dropTo].Role == ChatRole.User)
            {
                break;
            }
            totalTokens -= messageTokens[dropTo];
            dropTo++;
        }

        // Don't crash the conversation: if the active turn alone exceeds the
        // budget there's nothing more we can drop here. Return whatever we've
        // pruned and let the provider surface the over-limit error — that's a
        // bug to fix elsewhere (e.g. cap snapshot size, summarise tool
        // results), not silently corrupt the active turn.
        if (dropTo == 0)
        {
            return new TrimResult(history, 0, totalTokens + fixedOverhead, budget + fixedOverhead);
        }

        var trimmed = new ChatMessage[history.Count - dropTo];
        for (var i = dropTo; i < history.Count; i++)
        {
            trimmed[i - dropTo] = history[i];
        }
        return new TrimResult(trimmed, dropTo, totalTokens + fixedOverhead, budget + fixedOverhead);
    }

    private static int EstimateFixedOverhead(string? systemPrompt, IReadOnlyList<ChatTool> tools)
    {
        var chars = systemPrompt?.Length ?? 0;
        foreach (var tool in tools)
        {
            chars += tool.Name.Length;
            chars += tool.Description.Length;
            chars += tool.JsonSchema.GetRawText().Length;
        }
        return CharsToTokens(chars);
    }

    private static int EstimateMessageTokens(ChatMessage message)
    {
        // Per-message wire overhead on both Anthropic and OpenAI is small
        // but non-zero (role markers, content-block envelopes). Add a flat
        // 8 tokens per block to absorb that.
        var chars = 0;
        var blockOverhead = 0;
        foreach (var block in message.Blocks)
        {
            blockOverhead += 8;
            switch (block)
            {
                case ChatContentBlock.TextBlock text:
                    chars += text.Text.Length;
                    break;
                case ChatContentBlock.ToolUseBlock toolUse:
                    chars += toolUse.Name.Length;
                    chars += toolUse.Args.GetRawText().Length;
                    break;
                case ChatContentBlock.ToolResultBlock toolResult:
                    chars += toolResult.Result.GetRawText().Length;
                    break;
            }
        }
        return CharsToTokens(chars) + blockOverhead;
    }

    private static int CharsToTokens(int chars) =>
        chars <= 0 ? 0 : (int)Math.Ceiling(chars / CharsPerTokenEstimate);
}
