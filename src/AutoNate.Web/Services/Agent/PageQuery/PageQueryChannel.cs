using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Agent.PageQuery;

// Scoped per-request implementation. AgentSession activates one of these at
// the start of every SendMessageAsync call, supplying the conversation id
// and an event-emit callback that pushes events to the SSE stream. Skills
// resolve IPageQueryChannel from DI and call AskAsync; this class allocates
// query ids, registers a TCS with the singleton router, emits a
// PageQueryRequested event, and awaits the SPA's reply.
public sealed class PageQueryChannel : IPageQueryChannel
{
    private readonly IPageQueryRouter _router;
    private readonly ILogger<PageQueryChannel> _logger;
    private Guid _conversationId;
    private Func<AgentEvent, ValueTask>? _emit;
    private bool _activated;

    public PageQueryChannel(IPageQueryRouter router, ILogger<PageQueryChannel> logger)
    {
        _router = router;
        _logger = logger;
    }

    // Wires up the conversation id and a callback that pushes AgentEvents
    // into the SSE-bound queue so AskAsync can emit PageQueryRequested
    // mid-tool-invocation. Only AgentSession should call this; it is public
    // so unit tests can drive the channel without spinning up the full DI
    // graph.
    public void Activate(Guid conversationId, Func<AgentEvent, ValueTask> emit)
    {
        _conversationId = conversationId;
        _emit = emit;
        _activated = true;
    }

    public void Deactivate()
    {
        _activated = false;
        _emit = null;
    }

    public async Task<PageQueryResult> AskAsync(string topic, JsonElement? args, CancellationToken cancellationToken)
    {
        if (!_activated || _emit is null)
        {
            return new PageQueryResult.Failure("page_unreachable", "No active page channel for this session.");
        }

        var queryId = Guid.NewGuid().ToString("N");
        var tcs = _router.Register(_conversationId, queryId);

        try
        {
            await _emit(new AgentEvent.PageQueryRequested(queryId, topic, args)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _router.Cleanup(_conversationId, queryId);
            _logger.LogWarning(ex, "Failed to emit PageQueryRequested for {ConversationId}/{QueryId}", _conversationId, queryId);
            return new PageQueryResult.Failure("emit_failed", ex.Message);
        }

        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PageQueryResult.Failure("cancelled", "Page query was cancelled.");
        }
        finally
        {
            _router.Cleanup(_conversationId, queryId);
        }
    }
}
