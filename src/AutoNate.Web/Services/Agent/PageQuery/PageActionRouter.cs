using System.Collections.Concurrent;

namespace AutoNate.Web.Services.Agent.PageQuery;

public sealed class PageActionRouter : IPageActionRouter
{
    private readonly ConcurrentDictionary<(Guid ConversationId, string ActionId), TaskCompletionSource<PageActionResult>> _pending = new();

    public TaskCompletionSource<PageActionResult> Register(Guid conversationId, string actionId)
    {
        var tcs = new TaskCompletionSource<PageActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd((conversationId, actionId), tcs))
        {
            throw new InvalidOperationException($"Duplicate page-action registration for {conversationId}/{actionId}.");
        }
        return tcs;
    }

    public void Cleanup(Guid conversationId, string actionId) =>
        _pending.TryRemove((conversationId, actionId), out _);

    public bool TryResolve(Guid conversationId, string actionId, PageActionResult result)
    {
        if (!_pending.TryRemove((conversationId, actionId), out var tcs)) return false;
        return tcs.TrySetResult(result);
    }
}
