using AutoNate.Web.Persistence;

namespace AutoNate.Web.Authorization.Edges;

// Writes entity_edges rows by attaching them to a caller-owned DbContext. The
// caller's transaction stays in charge of commit/rollback so the edge writes
// are atomic with the resource mutation that triggered them.
public interface IEntityEdgeWriter
{
    void AddEdge(
        AutoNateDbContext db,
        string edgeKind,
        string fromKind,
        string fromId,
        string toKind,
        string toId,
        Guid actorId,
        DateTimeOffset when);

    Task RemoveEdgeAsync(
        AutoNateDbContext db,
        string edgeKind,
        string fromKind,
        string fromId,
        string toKind,
        string toId,
        CancellationToken cancellationToken = default);

    // Diffs an existing set of user principals against an incoming set and
    // applies the necessary additions/removals. The User→Resource direction
    // mirrors EdgeKinds.Assignee / EdgeKinds.Creator.
    Task SyncUserEdgesAsync(
        AutoNateDbContext db,
        string edgeKind,
        string toKind,
        string toId,
        IReadOnlyCollection<Guid> oldUserIds,
        IReadOnlyCollection<Guid> newUserIds,
        Guid actorId,
        DateTimeOffset when,
        CancellationToken cancellationToken = default);
}
