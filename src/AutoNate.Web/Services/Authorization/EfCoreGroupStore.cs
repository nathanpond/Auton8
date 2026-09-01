using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Authorization;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using GroupEntity = AutoNate.Web.Persistence.Scaffolded.Group;
using GroupMemberEntity = AutoNate.Web.Persistence.Scaffolded.GroupMember;

namespace AutoNate.Web.Services.Authorization;

public sealed class EfCoreGroupStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IAuthorizer authorizer) : IGroupStore
{
    public async Task<IReadOnlyList<Group>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Groups.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(g => !g.IsArchived);
        }

        var rows = await query.OrderBy(g => g.Name).ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<Group>> ListAuthorizedAsync(
        ClaimsPrincipal actor,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<GroupEntity> query = db.Groups.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(g => !g.IsArchived);
        }

        var visible = await authorizer.FilterQueryAsync(
            db, actor, EntityKinds.Group, Actions.View, query.OrderBy(g => g.Name), cancellationToken);
        var rows = await visible.ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<Group?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<Group> CreateAsync(CreateGroupInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new GroupValidationException("name is required.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Groups.AnyAsync(g => g.Name == name, cancellationToken))
        {
            throw new GroupValidationException($"Group '{name}' already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new GroupEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = input.Description?.Trim(),
            IsArchived = false,
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId
        };
        db.Groups.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<Group> UpdateAsync(Guid id, UpdateGroupInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new GroupNotFoundException(id);

        var changed = false;
        if (input.Name is { } newNameRaw)
        {
            var newName = newNameRaw.Trim();
            if (newName.Length == 0)
            {
                throw new GroupValidationException("name cannot be empty.");
            }

            if (!string.Equals(entity.Name, newName, StringComparison.Ordinal))
            {
                if (await db.Groups.AnyAsync(g => g.Name == newName && g.Id != id, cancellationToken))
                {
                    throw new GroupValidationException($"Group '{newName}' already exists.");
                }

                entity.Name = newName;
                changed = true;
            }
        }

        if (input.Description is { } newDescRaw)
        {
            var newDesc = string.IsNullOrWhiteSpace(newDescRaw) ? null : newDescRaw.Trim();
            if (!string.Equals(entity.Description, newDesc, StringComparison.Ordinal))
            {
                entity.Description = newDesc;
                changed = true;
            }
        }

        if (changed)
        {
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.UpdatedBy = actorId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToModel(entity);
    }

    public async Task<Group> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new GroupNotFoundException(id);

        if (entity.IsArchived == archived)
        {
            return ToModel(entity);
        }

        entity.IsArchived = archived;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;
        await db.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.Groups.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<GroupMember>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.AddedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<Group>> ListGroupsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
            from g in db.Groups.AsNoTracking()
            join m in db.GroupMembers.AsNoTracking() on g.Id equals m.GroupId
            where m.UserId == userId
            orderby g.Name
            select g).ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<bool> AddMemberAsync(Guid groupId, Guid userId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken);
        if (!groupExists)
        {
            throw new GroupNotFoundException(groupId);
        }

        var alreadyMember = await db.GroupMembers
            .AnyAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken);
        if (alreadyMember)
        {
            return false;
        }

        db.GroupMembers.Add(new GroupMemberEntity
        {
            GroupId = groupId,
            UserId = userId,
            AddedAtUtc = DateTime.UtcNow,
            AddedBy = actorId
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.GroupMembers
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.GroupMembers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Group ToModel(GroupEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        IsArchived = e.IsArchived,
        CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
        CreatedBy = e.CreatedBy,
        UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc),
        UpdatedBy = e.UpdatedBy
    };

    private static GroupMember ToModel(GroupMemberEntity e) => new()
    {
        GroupId = e.GroupId,
        UserId = e.UserId,
        AddedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.AddedAtUtc),
        AddedBy = e.AddedBy
    };
}
