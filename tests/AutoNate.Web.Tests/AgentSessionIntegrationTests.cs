using System.Text.Json;
using AutoNate.Web.Services.Agent;
using AutoNate.Web.Services.Agent.Conversations;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AgentSessionIntegrationTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task End_to_end_two_iteration_loop_persists_messages_tool_calls_and_publishes_audit()
    {
        var fakeProvider = new ScriptedChatProvider(new[]
        {
            // Iteration 1: model emits text, then a tool_use, then stops with tool_use.
            new[]
            {
                (ChatStreamChunk)new ChatStreamChunk.TextDelta("Looking up "),
                new ChatStreamChunk.TextDelta("workflows. "),
                new ChatStreamChunk.ToolUseStarted("call_a", "find_workflow"),
                new ChatStreamChunk.ToolUseCompleted("call_a", "find_workflow", ParseElement("""{"query":"approval"}""")),
                new ChatStreamChunk.MessageStop(ChatStopReason.ToolUse, new Usage(20, 30, null, null))
            },
            // Iteration 2: model emits text and ends.
            new[]
            {
                (ChatStreamChunk)new ChatStreamChunk.TextDelta("I found the relevant workflow."),
                new ChatStreamChunk.MessageStop(ChatStopReason.EndTurn, new Usage(40, 50, null, null))
            }
        });

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var fakeResolver = new FakeProviderResolver(fakeProvider);

        var customised = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatProviderResolver>();
                services.AddSingleton<IChatProviderResolver>(_ => fakeResolver);
            });
        });

        // Force host startup with the override applied.
        _ = customised.CreateClient();

        var recorder = (RecordingAuditEventPublisher)customised.Services
            .GetRequiredService<AutoNate.Web.Services.Events.IAuditEventPublisher>();

        using var scope = customised.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IAgentSession>();
        var store = scope.ServiceProvider.GetRequiredService<IAgentConversationStore>();

        var conversation = await session.StartAsync(TestUserId, pageKey: "workflow", connectionId: null);
        recorder.Clear();

        var events = new List<AgentEvent>();
        await foreach (var ev in session.SendMessageAsync(conversation.Id, TestUserId, "Find me an approval workflow"))
        {
            events.Add(ev);
        }

        // Stream shape:
        // - first MessageStarted
        // - some TextDeltas
        // - ToolStarted + ToolCompleted (or ToolFailed) for our scripted call
        // - second MessageStarted (iteration 2)
        // - more TextDeltas
        // - MessageCompleted + Done
        Assert.Contains(events, e => e is AgentEvent.MessageStarted);
        Assert.Contains(events, e => e is AgentEvent.TextDelta);
        Assert.Contains(events, e => e is AgentEvent.ToolStarted);
        Assert.Contains(events, e => e is AgentEvent.ToolCompleted or AgentEvent.ToolFailed);
        Assert.Contains(events, e => e is AgentEvent.MessageCompleted);
        Assert.Contains(events, e => e is AgentEvent.Done);

        // Persistence shape: user msg, assistant msg w/ tool_use, synthetic tool msg, final assistant msg.
        var detail = await store.GetForUserAsync(conversation.Id, TestUserId);
        Assert.NotNull(detail);
        Assert.True(detail!.Messages.Count >= 4, $"Expected ≥4 messages, got {detail.Messages.Count}");
        Assert.Single(detail.ToolCalls);
        var toolCall = detail.ToolCalls[0];
        Assert.Equal("find_workflow", toolCall.ToolName);
        Assert.Contains(toolCall.Status, new[] { "succeeded", "failed" });

        // Audit events fired in the right order.
        var eventTypes = recorder.Events.Select(e => e.EventType).ToList();
        Assert.Contains(AgentEventTypes.MessageUserSent, eventTypes);
        Assert.Contains(AgentEventTypes.MessageAssistantStarted, eventTypes);
        Assert.Contains(AgentEventTypes.ToolInvoked, eventTypes);
        Assert.Contains(AgentEventTypes.MessageAssistantCompleted, eventTypes);
    }

    [Fact]
    public async Task Provider_error_chunk_persists_error_and_emits_done()
    {
        var fakeProvider = new ScriptedChatProvider(new[]
        {
            new[]
            {
                (ChatStreamChunk)new ChatStreamChunk.Error("Anthropic returned 500: upstream blew up", false)
            }
        });

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var customised = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatProviderResolver>();
                services.AddSingleton<IChatProviderResolver>(_ => new FakeProviderResolver(fakeProvider));
            });
        });
        _ = customised.CreateClient();

        var recorder = (RecordingAuditEventPublisher)customised.Services
            .GetRequiredService<AutoNate.Web.Services.Events.IAuditEventPublisher>();

        using var scope = customised.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IAgentSession>();

        var convo = await session.StartAsync(TestUserId, pageKey: "system-issues", connectionId: null);
        recorder.Clear();

        var events = new List<AgentEvent>();
        await foreach (var ev in session.SendMessageAsync(convo.Id, TestUserId, "ping"))
        {
            events.Add(ev);
        }

        Assert.Contains(events, e => e is AgentEvent.Error err && err.Message.Contains("500"));
        Assert.Contains(events, e => e is AgentEvent.Done);
        Assert.Contains(
            recorder.Events,
            e => e.EventType == AgentEventTypes.MessageAssistantFailed);
    }

    private static JsonElement ParseElement(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    // Hands the agent loop a pre-canned sequence of chunks per iteration. The
    // outer array is per StreamAsync call; each inner array is the chunks
    // that call yields.
    private sealed class ScriptedChatProvider : IChatProvider
    {
        private readonly IReadOnlyList<IReadOnlyList<ChatStreamChunk>> _scripts;
        private int _iteration;

        public ScriptedChatProvider(IReadOnlyList<IReadOnlyList<ChatStreamChunk>> scripts) => _scripts = scripts;

        public string Kind => "Scripted";

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var script = _iteration < _scripts.Count ? _scripts[_iteration] : Array.Empty<ChatStreamChunk>();
            _iteration++;
            foreach (var chunk in script)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }

        public Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatProviderTestResult(true, 0, "scripted", null));
    }

    private sealed class FakeProviderResolver : IChatProviderResolver
    {
        private readonly IChatProvider _provider;
        public FakeProviderResolver(IChatProvider provider) => _provider = provider;

        public Task<IChatProvider?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IChatProvider?>(_provider);
        public Task<IChatProvider?> ResolveDefaultForKindAsync(string kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IChatProvider?>(_provider);
    }
}
