using System.Text.Json;

namespace AutoNate.Web.Services.Agent.PageQuery;

// Per-conversation handle resolved from DI by skills that want to ask the
// SPA for fresh or larger slices of page state. Activated by AgentSession
// at the start of SendMessageAsync; once activated, AskAsync emits a
// PageQueryRequested event toward the SPA over the SSE stream and awaits
// the SPA's reply via IPageQueryRouter.
//
// When not activated (e.g. resolved outside an active session, which should
// not happen in normal flows), AskAsync returns a Failure result rather
// than throwing — calling tools degrade gracefully.
public interface IPageQueryChannel
{
    Task<PageQueryResult> AskAsync(string topic, JsonElement? args, CancellationToken cancellationToken);
}
