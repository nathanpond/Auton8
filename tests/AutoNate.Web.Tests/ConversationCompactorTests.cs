using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Providers;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class ConversationCompactorTests
{
    [Fact]
    public async Task Returns_null_when_history_is_too_short_to_compact()
    {
        var history = new[]
        {
            new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("hi") }),
            new ChatMessage(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock("hello") })
        };

        var compactor = new ConversationCompactor();
        var result = await compactor.CompactAsync(
            new StubProvider(new[] { new ChatStreamChunk[] { new ChatStreamChunk.TextDelta("nope") } }),
            new ConversationCompactor.CompactInput(
                History: history,
                HistoryIds: history.Select((m, i) => new ConversationCompactor.MessageIdentity(Guid.NewGuid(), m)).ToList(),
                ContextWindowTokens: 200_000,
                MaxOutputTokens: 4096,
                SystemPrompt: null,
                Tools: Array.Empty<ChatTool>()));

        Assert.Null(result);
    }

    [Fact]
    public async Task Compacts_prefix_into_summary_text_anchored_on_a_user_message()
    {
        // 12-message conversation (6 user+assistant pairs). MinimumPreservedTailMessages
        // is 6, so the compactor should leave the last 6 messages alone and
        // summarize the first 6.
        var history = new List<ChatMessage>();
        var ids = new List<ConversationCompactor.MessageIdentity>();
        for (var i = 0; i < 6; i++)
        {
            var u = new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock($"q{i}") });
            var a = new ChatMessage(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock($"a{i}") });
            history.Add(u);
            history.Add(a);
            ids.Add(new ConversationCompactor.MessageIdentity(Guid.NewGuid(), u));
            ids.Add(new ConversationCompactor.MessageIdentity(Guid.NewGuid(), a));
        }

        var compactor = new ConversationCompactor();
        var provider = new StubProvider(new[]
        {
            new ChatStreamChunk[]
            {
                new ChatStreamChunk.TextDelta("user discussed Q0..Q2; "),
                new ChatStreamChunk.TextDelta("assistant suggested ABC."),
                new ChatStreamChunk.MessageStop(ChatStopReason.EndTurn, new Usage(123, 45, null, null))
            }
        });

        var result = await compactor.CompactAsync(
            provider,
            new ConversationCompactor.CompactInput(
                History: history,
                HistoryIds: ids,
                ContextWindowTokens: 200_000,
                MaxOutputTokens: 4096,
                SystemPrompt: null,
                Tools: Array.Empty<ChatTool>()));

        Assert.NotNull(result);
        Assert.Equal("user discussed Q0..Q2; assistant suggested ABC.", result!.SummaryText);
        Assert.Equal(45, result.Usage?.OutputTokens);
        // The split must land on a user message — that's what
        // ChooseSplitIndex guarantees so the surviving tail starts cleanly.
        Assert.Equal(ChatRole.User, history[result.PrefixCount].Role);
        // The recorded "replaces through" id must be the message *just before*
        // the split point, never inside the preserved tail.
        Assert.Equal(ids[result.PrefixCount - 1].MessageId, result.ReplacesThroughMessageId);
    }

    [Fact]
    public async Task Returns_null_when_provider_emits_an_error_chunk()
    {
        var history = BuildLongHistory();
        var ids = history.Select((m, _) => new ConversationCompactor.MessageIdentity(Guid.NewGuid(), m)).ToList();
        var provider = new StubProvider(new[]
        {
            new ChatStreamChunk[]
            {
                new ChatStreamChunk.TextDelta("partial..."),
                new ChatStreamChunk.Error("rate limited", IsRetryable: true)
            }
        });

        var compactor = new ConversationCompactor();
        var result = await compactor.CompactAsync(
            provider,
            new ConversationCompactor.CompactInput(
                History: history,
                HistoryIds: ids,
                ContextWindowTokens: 200_000,
                MaxOutputTokens: 4096,
                SystemPrompt: null,
                Tools: Array.Empty<ChatTool>()));

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_when_provider_throws()
    {
        var history = BuildLongHistory();
        var ids = history.Select((m, _) => new ConversationCompactor.MessageIdentity(Guid.NewGuid(), m)).ToList();
        var provider = new ThrowingProvider();

        var compactor = new ConversationCompactor();
        var result = await compactor.CompactAsync(
            provider,
            new ConversationCompactor.CompactInput(
                History: history,
                HistoryIds: ids,
                ContextWindowTokens: 200_000,
                MaxOutputTokens: 4096,
                SystemPrompt: null,
                Tools: Array.Empty<ChatTool>()));

        Assert.Null(result);
    }

    [Fact]
    public void ChooseSplitIndex_lands_on_a_user_message_or_returns_zero()
    {
        var history = BuildLongHistory();
        var split = ConversationCompactor.ChooseSplitIndex(history);
        Assert.True(split > 0);
        Assert.Equal(ChatRole.User, history[split].Role);
    }

    [Fact]
    public async Task TailOverride_lets_short_conversations_compact()
    {
        // 4 messages: under default MinimumPreservedTailMessages=6 the
        // compactor would refuse. With TailOverride=2 it should still
        // proceed (the overflow-retry path needs this).
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("q1 with lots of context") }),
            new(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock("a1") }),
            new(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("q2") }),
            new(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock("a2") })
        };
        var ids = history.Select(m => new ConversationCompactor.MessageIdentity(Guid.NewGuid(), m)).ToList();
        var provider = new StubProvider(new[]
        {
            new ChatStreamChunk[]
            {
                new ChatStreamChunk.TextDelta("rolled up q1/a1."),
                new ChatStreamChunk.MessageStop(ChatStopReason.EndTurn, null)
            }
        });

        var compactor = new ConversationCompactor();
        var result = await compactor.CompactAsync(
            provider,
            new ConversationCompactor.CompactInput(
                History: history,
                HistoryIds: ids,
                ContextWindowTokens: 200_000,
                MaxOutputTokens: 4096,
                SystemPrompt: null,
                Tools: Array.Empty<ChatTool>(),
                TailOverride: 2));

        Assert.NotNull(result);
        Assert.Equal("rolled up q1/a1.", result!.SummaryText);
    }

    [Fact]
    public async Task Drops_oldest_prefix_messages_so_summary_call_fits()
    {
        // Build a giant prefix (single message that easily exceeds the
        // model's window). The compactor must drop oldest prefix entries
        // until the *summary call* itself fits, otherwise summarization
        // recursively 400s. We assert the call still succeeds even when
        // the raw prefix would have blown the budget.
        var huge = new string('a', 200_000); // ~80K tokens at 2.5 chars/token
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock(huge) }),
            new(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock(huge) }),
            new(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock(huge) }),
            new(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock("recent") }),
            new(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("active") }),
            new(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock("ok") }),
            new(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock("active2") }),
            new(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock("ok2") })
        };
        var ids = history.Select(m => new ConversationCompactor.MessageIdentity(Guid.NewGuid(), m)).ToList();

        // Capture the prefix the compactor sends to the provider.
        var captured = new CapturingProvider();
        var compactor = new ConversationCompactor();
        var result = await compactor.CompactAsync(
            captured,
            new ConversationCompactor.CompactInput(
                History: history,
                HistoryIds: ids,
                ContextWindowTokens: 100_000, // smaller window forces trim
                MaxOutputTokens: 4096,
                SystemPrompt: null,
                Tools: Array.Empty<ChatTool>()));

        Assert.NotNull(result);
        Assert.NotNull(captured.LastRequest);

        // The prefix actually sent to the provider must be smaller than the
        // raw split prefix would have been — TrimPrefixToBudget kicked in.
        var split = ConversationCompactor.ChooseSplitIndex(history);
        Assert.True(split > 0);
        Assert.True(captured.LastRequest!.Messages.Count < split,
            $"Expected prefix to be trimmed below {split}; got {captured.LastRequest.Messages.Count}.");
    }

    private static IReadOnlyList<ChatMessage> BuildLongHistory()
    {
        var history = new List<ChatMessage>();
        for (var i = 0; i < 6; i++)
        {
            history.Add(new ChatMessage(ChatRole.User, new ChatContentBlock[] { new ChatContentBlock.TextBlock($"q{i}") }));
            history.Add(new ChatMessage(ChatRole.Assistant, new ChatContentBlock[] { new ChatContentBlock.TextBlock($"a{i}") }));
        }
        return history;
    }

    private sealed class StubProvider : IChatProvider
    {
        private readonly IReadOnlyList<IReadOnlyList<ChatStreamChunk>> _scripts;
        private int _i;

        public StubProvider(IReadOnlyList<IReadOnlyList<ChatStreamChunk>> scripts) => _scripts = scripts;

        public string Kind => "Stub";
        public string ModelId => "stub-model";

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var script = _i < _scripts.Count ? _scripts[_i] : Array.Empty<ChatStreamChunk>();
            _i++;
            foreach (var chunk in script)
            {
                yield return chunk;
                await Task.Yield();
            }
        }

        public Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatProviderTestResult(true, 0, "stub", null));
    }

    private sealed class CapturingProvider : IChatProvider
    {
        public ChatRequest? LastRequest { get; private set; }

        public string Kind => "Capturing";
        public string ModelId => "capturing-model";

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            yield return new ChatStreamChunk.TextDelta("captured summary.");
            yield return new ChatStreamChunk.MessageStop(ChatStopReason.EndTurn, null);
            await Task.Yield();
        }

        public Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatProviderTestResult(true, 0, "captured", null));
    }

    private sealed class ThrowingProvider : IChatProvider
    {
        public string Kind => "Throwing";
        public string ModelId => "throwing-model";

        public IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
            ThrowAsync(cancellationToken);

        private static async IAsyncEnumerable<ChatStreamChunk> ThrowAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("provider unavailable");
#pragma warning disable CS0162 // unreachable; required for compiler to recognize the iterator.
            yield break;
#pragma warning restore CS0162
        }

        public Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatProviderTestResult(true, 0, "throwing", null));
    }
}
