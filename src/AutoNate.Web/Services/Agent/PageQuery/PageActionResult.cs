using System.Text.Json;

namespace AutoNate.Web.Services.Agent.PageQuery;

// Result of a backend → SPA action request. Always returns a structured
// outcome envelope: on success, an applied summary plus optional changes
// the model can reference; on failure, a code + human message. Even if
// the action is logically a no-op, success carries the summary so the
// model can confirm completion to the user.
public abstract record class PageActionResult
{
    public abstract bool Ok { get; }

    public sealed record class Success(string Summary, JsonElement? Changes = null) : PageActionResult
    {
        public override bool Ok => true;
    }

    public sealed record class Failure(string ErrorCode, string? Message = null) : PageActionResult
    {
        public override bool Ok => false;
    }
}
