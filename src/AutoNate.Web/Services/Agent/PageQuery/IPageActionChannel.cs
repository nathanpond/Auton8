using System.Text.Json;

namespace AutoNate.Web.Services.Agent.PageQuery;

// Per-conversation handle for sending mutating actions to the SPA. Resolves
// from DI to the same scoped instance for both AgentSession (which activates
// it) and skills (which call ApplyAsync). Pages register page-specific
// actions via PageContextProviderEntry.onPageAction; the framework also
// auto-handles a built-in form-fill action set on the SPA side.
public interface IPageActionChannel
{
    Task<PageActionResult> ApplyAsync(string action, JsonElement? args, CancellationToken cancellationToken);
}
