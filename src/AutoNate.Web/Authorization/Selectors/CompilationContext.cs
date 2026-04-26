using AutoNate.Web.Persistence;

namespace AutoNate.Web.Authorization.Selectors;

// Per-request, per-call context handed to selector compilers. Holds the
// DbContext so compiled expressions can reference cross-table subqueries
// (e.g. JOINs against entity_edges) and the actor identity so `user` literals
// resolve correctly.
public sealed class CompilationContext
{
    public CompilationContext(AutoNateDbContext db, Guid actorUserId)
    {
        Db = db ?? throw new ArgumentNullException(nameof(db));
        ActorUserId = actorUserId;
    }

    public AutoNateDbContext Db { get; }

    public Guid ActorUserId { get; }

    public string ActorUserIdString => ActorUserId.ToString();
}
