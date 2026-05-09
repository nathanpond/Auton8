using System.Collections.Concurrent;

namespace AutoNate.Web.Services.Agent.PageQuery;

public sealed class PageQueryRouter : IPageQueryRouter
{
    private readonly ConcurrentDictionary<(Guid ConversationId, string QueryId), TaskCompletionSource<PageQueryResult>> _pending = new();

    public TaskCompletionSource<PageQueryResult> Register(Guid conversationId, string queryId)
    {
        // RunContinuationsAsynchronously avoids running the awaiting code on
        // the POST endpoint's request thread, which would otherwise tie up
        // ASP.NET's response pipeline.
        var tcs = new TaskCompletionSource<PageQueryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd((conversationId, queryId), tcs))
        {
            throw new InvalidOperationException($"Duplicate page-query registration for {conversationId}/{queryId}.");
        }
        return tcs;
    }

    public void Cleanup(Guid conversationId, string queryId) =>
        _pending.TryRemove((conversationId, queryId), out _);

    public bool TryResolve(Guid conversationId, string queryId, PageQueryResult result)
    {
        if (!_pending.TryRemove((conversationId, queryId), out var tcs)) return false;
        return tcs.TrySetResult(result);
    }
}
