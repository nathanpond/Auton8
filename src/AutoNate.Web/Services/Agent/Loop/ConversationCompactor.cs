using System.Text;
using AutoNate.Web.Services.Agent.Providers;

namespace AutoNate.Web.Services.Agent.Loop;

// Rolls up the oldest portion of a conversation into a single summary text
// so the next prompt fits inside the model's context window. The agent
// loop calls this when estimated input tokens cross
// CompactionTriggerFraction; if it succeeds, the prefix is replaced in
// memory and persisted as a kind='summary' row. If it fails (provider
// error, transient timeout, malformed response) the loop falls back to the
// existing ConversationHistoryTrimmer so the user still gets a response.
public sealed class ConversationCompactor
{
    // Trigger compaction once we cross 60% of the context window. Bumped
    // down from 70% after a 200K conversation came in just over the cap —
    // earlier compaction means the request still fits even when a tool
    // result arrives that's denser-per-token than our estimator assumed.
    public const double CompactionTriggerFraction = 0.60;

    // Always keep the most recent K messages verbatim — exact identifiers
    // and tool outputs from recent turns are typically what the user is
    // about to reference. Six covers a 2-3 turn ping-pong with tools.
    public const int MinimumPreservedTailMessages = 6;

    // How many tokens we let the summarization request itself produce.
    // Long enough to capture decisions and open threads; short enough that
    // it doesn't blow a chunk of the budget we just freed.
    public const int SummaryMaxOutputTokens = 1_500;

    private const string SummarySystemPrompt =
        "You are summarizing the earlier portion of a conversation between a user and an AI assistant so a fresh assistant turn can continue without losing context. " +
        "Produce a single dense paragraph (or a short bulleted list when there are clearly distinct threads) that captures: " +
        "(1) what the user is trying to accomplish, " +
        "(2) decisions and constraints already established, " +
        "(3) specific identifiers, names, file paths, ids, or values the user has referenced, " +
        "(4) tool calls that were made and their salient outputs, " +
        "(5) open questions or pending follow-ups. " +
        "Do not address the user. Do not roleplay. Do not propose next steps. " +
        "Write in third person past tense (e.g. \"the user asked… the assistant suggested…\"). " +
        "Be specific over poetic — preserving an exact id is more useful than a clever sentence.";

    public sealed record class CompactInput(
        IReadOnlyList<ChatMessage> History,
        IReadOnlyList<MessageIdentity> HistoryIds,
        int ContextWindowTokens,
        int MaxOutputTokens,
        string? SystemPrompt,
        IReadOnlyList<ChatTool> Tools,
        // When set, the compactor preserves only this many messages in the
        // tail instead of MinimumPreservedTailMessages. Used by the
        // context-overflow retry path: if the normal split still produces a
        // tail too big to fit, we re-run with TailOverride=2 to summarize
        // even more aggressively.
        int? TailOverride = null);

    // Pairs a ChatMessage with the persisted message id behind it. The
    // compactor needs the id of the last subsumed row so AppendSummaryAsync
    // can store a precise replaces_through pointer for audit.
    public sealed record class MessageIdentity(Guid MessageId, ChatMessage Message);

    public sealed record class CompactOutput(
        string SummaryText,
        Guid ReplacesThroughMessageId,
        int PrefixCount,
        Usage? Usage);

    public async Task<CompactOutput?> CompactAsync(
        IChatProvider provider,
        CompactInput input,
        CancellationToken cancellationToken = default)
    {
        var tailSize = input.TailOverride ?? MinimumPreservedTailMessages;
        // Safety: if the conversation is too short there's nothing useful to
        // roll up. The trimmer (or an honest 400) is the right path then.
        if (input.History.Count <= tailSize + 1)
        {
            return null;
        }
        if (input.HistoryIds.Count != input.History.Count)
        {
            return null;
        }

        // Carve a prefix to summarize and a tail to keep. The split point
        // is "first user message that lets us keep at least tailSize
        // messages" so we never strand a tool call away from its
        // tool_result.
        var splitIndex = ChooseSplitIndex(input.History, tailSize);
        if (splitIndex <= 0) return null;

        // Hard-trim the prefix so the summarization request itself can't
        // blow the same context window we're trying to ease. The summary
        // call carries no tool schemas and a short system prompt, so the
        // budget is most of the context window.
        var prefix = TrimPrefixToBudget(
            input.History,
            splitIndex,
            input.ContextWindowTokens);
        if (prefix.Length == 0) return null;

        var summaryRequest = new ChatRequest(
            Messages: prefix,
            SystemPrompt: SummarySystemPrompt,
            Tools: Array.Empty<ChatTool>(),
            ModelId: provider.ModelId,
            MaxTokens: SummaryMaxOutputTokens);

        var buffer = new StringBuilder();
        Usage? usage = null;
        try
        {
            await foreach (var chunk in provider.StreamAsync(summaryRequest, cancellationToken).ConfigureAwait(false))
            {
                switch (chunk)
                {
                    case ChatStreamChunk.TextDelta td:
                        buffer.Append(td.Delta);
                        break;
                    case ChatStreamChunk.MessageStop ms:
                        usage = ms.Usage;
                        break;
                    case ChatStreamChunk.Error err:
                        // Refuse to persist an empty / partial summary on
                        // provider error — caller falls back to trimming.
                        return null;
                }
            }
        }
        catch
        {
            // Same fallback contract: anything thrown means we don't have a
            // trustworthy summary, so let the trimmer handle this turn.
            return null;
        }

        var text = buffer.ToString().Trim();
        if (text.Length == 0) return null;

        var lastSubsumedId = input.HistoryIds[splitIndex - 1].MessageId;
        return new CompactOutput(text, lastSubsumedId, splitIndex, usage);
    }

    // Find the index where the keep-tail begins. We walk back from the end,
    // counting messages, and lock in the first index that (a) is a user
    // message and (b) keeps at least `minTail` messages in the tail.
    // Anchoring on a user message means the tail starts on a role the
    // provider is happy to take as the next turn boundary.
    public static int ChooseSplitIndex(IReadOnlyList<ChatMessage> history, int? minTail = null)
    {
        if (history.Count == 0) return 0;
        var tail = minTail ?? MinimumPreservedTailMessages;
        for (var i = history.Count - tail; i > 0; i--)
        {
            if (i < history.Count && history[i].Role == ChatRole.User)
            {
                return i;
            }
        }
        return 0;
    }

    // Drop oldest messages from the prefix until its char-based estimate
    // fits inside (contextWindow - SummaryMaxOutputTokens - safety). Without
    // this the compactor can recursively 400 on a conversation that's
    // already past the cap. We use the same chars/2.5 heuristic the trimmer
    // does so the two stay in sync.
    private static ChatMessage[] TrimPrefixToBudget(
        IReadOnlyList<ChatMessage> history,
        int splitIndex,
        int contextWindowTokens)
    {
        const double charsPerToken = 2.5;
        // Safety pad inside the compaction call. Smaller than the trimmer's
        // because the summary request has no tools schema and a short
        // system prompt.
        const int summaryRequestSafety = 4_000;
        var budget = Math.Max(8_000, contextWindowTokens - SummaryMaxOutputTokens - summaryRequestSafety);

        var prefixList = new List<ChatMessage>(splitIndex);
        for (var i = 0; i < splitIndex; i++) prefixList.Add(history[i]);

        var totalTokens = 0;
        foreach (var msg in prefixList) totalTokens += EstimateTokens(msg, charsPerToken);

        // Drop from the front (oldest) until under budget. We don't preserve
        // tool_use/tool_result pairing here because we're handing the prefix
        // to a summarization call, not the live agent loop — provider-side
        // strictness about adjacency doesn't apply.
        while (totalTokens > budget && prefixList.Count > 0)
        {
            totalTokens -= EstimateTokens(prefixList[0], charsPerToken);
            prefixList.RemoveAt(0);
        }
        return prefixList.ToArray();
    }

    private static int EstimateTokens(ChatMessage message, double charsPerToken)
    {
        var chars = 0;
        var blockOverhead = 0;
        foreach (var block in message.Blocks)
        {
            blockOverhead += 8;
            switch (block)
            {
                case ChatContentBlock.TextBlock t:
                    chars += t.Text.Length;
                    break;
                case ChatContentBlock.ToolUseBlock tu:
                    chars += tu.Name.Length + tu.Args.GetRawText().Length;
                    break;
                case ChatContentBlock.ToolResultBlock tr:
                    chars += tr.Result.GetRawText().Length;
                    break;
            }
        }
        return blockOverhead + (chars <= 0 ? 0 : (int)Math.Ceiling(chars / charsPerToken));
    }
}
