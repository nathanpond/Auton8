namespace AutoNate.Web.Services.Agent.Catalog;

// Read-side service for the agent model catalog. Backed by an in-memory
// snapshot of the agent_model table so the chat-loop hot path doesn't pay
// a DB round-trip per turn. Writes go through IAgentModelCatalogStore,
// which calls Invalidate() to force the next read to refresh.
public interface IAgentModelCatalog
{
    // Returns the context window for the given model id. Uses longest-
    // prefix match so families like "claude-sonnet-4-6-20250514" resolve
    // through "claude-sonnet-4-6" if the exact id isn't registered. Falls
    // back to UnknownModelContextWindow for ids that don't match anything.
    int GetContextWindow(string? modelId);

    // True when the catalog has an entry whose model id matches (exactly
    // or by longest prefix). Used by the admin UI to flag dropdown
    // entries as "(est.)" when the catalog had to fall back.
    bool IsKnown(string? modelId);

    // Snapshot of all non-archived rows. Stale-tolerant — IConnectionModelLister
    // and the agent loop can call this anywhere without worrying about
    // contention. Refreshed on demand when Invalidate() is called.
    IReadOnlyList<AgentModelRow> All();

    // Force the next lookup to re-read from the store. Called after any
    // create/update/archive on the store side.
    void Invalidate();

    // Conservative window for ids the catalog doesn't recognise. Kept
    // pessimistic so the trimmer overshoots toward safety on unknown ids.
    int UnknownModelContextWindow { get; }

    // Returns the catalog's single default row, or null if none. The
    // chatbot uses this when a connection doesn't pin a model and the
    // default's provider matches the connection's provider.
    AgentModelRow? GetDefault();

    // Returns the first available, non-archived row for the given
    // provider, or null. Used by ChatProviderResolver when the global
    // default doesn't match the connection's provider.
    AgentModelRow? GetFirstAvailable(string provider);
}
