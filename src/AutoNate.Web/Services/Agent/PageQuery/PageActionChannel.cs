using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Agent.PageQuery;

// Scoped per-request implementation. Activated by AgentSession at the start
// of each tool invocation; the supplied emit callback writes events into
// the SSE stream so PageActionRequested reaches the SPA in real time.
// Skills resolve IPageActionChannel from DI and call ApplyAsync, which
// allocates an action id, registers a TCS with the singleton router, emits
// the request, and awaits the SPA's reply.
public sealed class PageActionChannel : IPageActionChannel
{
    private readonly IPageActionRouter _router;
    private readonly ILogger<PageActionChannel> _logger;
    private Guid _conversationId;
    private Func<AgentEvent, ValueTask>? _emit;
    private bool _activated;

    public PageActionChannel(IPageActionRouter router, ILogger<PageActionChannel> logger)
    {
        _router = router;
        _logger = logger;
    }

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

    public async Task<PageActionResult> ApplyAsync(string action, JsonElement? args, CancellationToken cancellationToken)
    {
        if (!_activated || _emit is null)
        {
            return new PageActionResult.Failure("page_unreachable", "No active page channel for this session.");
        }

        var actionId = Guid.NewGuid().ToString("N");
        var tcs = _router.Register(_conversationId, actionId);

        try
        {
            await _emit(new AgentEvent.PageActionRequested(actionId, action, args)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _router.Cleanup(_conversationId, actionId);
            _logger.LogWarning(ex, "Failed to emit PageActionRequested for {ConversationId}/{ActionId}", _conversationId, actionId);
            return new PageActionResult.Failure("emit_failed", ex.Message);
        }

        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PageActionResult.Failure("cancelled", "Page action was cancelled.");
        }
        finally
        {
            _router.Cleanup(_conversationId, actionId);
        }
    }
}
