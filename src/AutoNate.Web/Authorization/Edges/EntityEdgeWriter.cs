using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using EntityEdgeEntity = AutoNate.Web.Persistence.Scaffolded.EntityEdge;

namespace AutoNate.Web.Authorization.Edges;

public sealed class EntityEdgeWriter : IEntityEdgeWriter
{
    public void AddEdge(
        AutoNateDbContext db,
        string edgeKind,
        string fromKind,
        string fromId,
        string toKind,
        string toId,
        Guid actorId,
        DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(toId);

        db.EntityEdges.Add(new EntityEdgeEntity
        {
            Id = Guid.NewGuid(),
            EdgeKind = edgeKind,
            FromKind = fromKind,
            FromId = fromId,
            ToKind = toKind,
            ToId = toId,
            Data = "{}",
            CreatedAtUtc = when.UtcDateTime,
            CreatedBy = actorId
        });
    }

    public async Task RemoveEdgeAsync(
        AutoNateDbContext db,
        string edgeKind,
        string fromKind,
        string fromId,
        string toKind,
        string toId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.EntityEdges
            .Where(e => e.EdgeKind == edgeKind
                     && e.FromKind == fromKind
                     && e.FromId == fromId
                     && e.ToKind == toKind
                     && e.ToId == toId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            db.EntityEdges.RemoveRange(existing);
        }
    }

    public async Task SyncUserEdgesAsync(
        AutoNateDbContext db,
        string edgeKind,
        string toKind,
        string toId,
        IReadOnlyCollection<Guid> oldUserIds,
        IReadOnlyCollection<Guid> newUserIds,
        Guid actorId,
        DateTimeOffset when,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(oldUserIds);
        ArgumentNullException.ThrowIfNull(newUserIds);

        var oldSet = oldUserIds.ToHashSet();
        var newSet = newUserIds.ToHashSet();

        var added = newSet.Except(oldSet);
        var removed = oldSet.Except(newSet);

        foreach (var userId in added)
        {
            AddEdge(db, edgeKind, EntityKinds.User, userId.ToString(), toKind, toId, actorId, when);
        }

        if (removed.Any())
        {
            var removedStrings = removed.Select(g => g.ToString()).ToList();
            var staleEdges = await db.EntityEdges
                .Where(e => e.EdgeKind == edgeKind
                         && e.FromKind == EntityKinds.User
                         && e.ToKind == toKind
                         && e.ToId == toId
                         && removedStrings.Contains(e.FromId))
                .ToListAsync(cancellationToken);

            if (staleEdges.Count > 0)
            {
                db.EntityEdges.RemoveRange(staleEdges);
            }
        }
    }
}
