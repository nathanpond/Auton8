using AutoNate.Web.Models;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Flowable.Cache;

// Detail-endpoint read path that prefers the cache, falls back to live
// Flowable on miss or staleness, and writes through the projection so the
// next read is fast. List endpoints and AQL queries don't go through this —
// they query the cache table directly and accept eventual consistency.
public interface IFlowableReadThrough
{
    Task<WorkflowExecutionCache?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default);
}
