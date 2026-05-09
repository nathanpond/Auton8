using System.Text.Json;

namespace AutoNate.Web.Services.Agent.PageQuery;

// Result of a backend → SPA round-trip request for live page state. Mirrors
// the SPA-side discriminated union: { ok: true, data } | { ok: false, error }.
// Skills receive this from IPageQueryChannel and decide how to surface it
// to the model.
public abstract record class PageQueryResult
{
    public abstract bool Ok { get; }

    public sealed record class Success(JsonElement Data) : PageQueryResult
    {
        public override bool Ok => true;
    }

    public sealed record class Failure(string ErrorCode, string? Message = null) : PageQueryResult
    {
        public override bool Ok => false;
    }
}
