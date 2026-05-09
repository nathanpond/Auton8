namespace AutoNate.Web.Services.Agent.PageQuery;

// Singleton bridge between the in-flight agent loop (which lives in one
// HTTP request scope) and the page-query-results POST endpoint (which lives
// in another HTTP request scope). Holds pending TaskCompletionSources keyed
// by (conversationId, queryId) so the POST handler can resolve the right
// awaiter.
public interface IPageQueryRouter
{
    // Register a pending query. The caller is the IPageQueryChannel inside
    // AgentSession; it awaits the returned Task. Throws if the same
    // (conversationId, queryId) pair is already registered.
    System.Threading.Tasks.TaskCompletionSource<PageQueryResult> Register(Guid conversationId, string queryId);

    // Remove a pending query without resolving it (used in finally blocks
    // to clean up after timeouts / cancellations).
    void Cleanup(Guid conversationId, string queryId);

    // Resolve a pending query with the SPA's reply. Returns false if the
    // query is no longer registered (timed out, cancelled, or stale).
    bool TryResolve(Guid conversationId, string queryId, PageQueryResult result);
}
