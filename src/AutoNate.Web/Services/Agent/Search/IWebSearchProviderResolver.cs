namespace AutoNate.Web.Services.Agent.Search;

// Mirrors IChatProviderResolver. WebSearchSkill calls ResolveDefaultAsync at
// invoke time, gets back a configured provider (or null if no admin has
// added one yet), and either issues the search or returns a friendly error
// envelope to the agent.
public interface IWebSearchProviderResolver
{
    Task<IWebSearchProvider?> ResolveDefaultAsync(CancellationToken cancellationToken = default);

    Task<IWebSearchProvider?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default);
}
