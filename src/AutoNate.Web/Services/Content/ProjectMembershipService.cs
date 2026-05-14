using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Content;

public sealed class ProjectMembershipService : IProjectMembershipService
{
    public async Task<ProjectRole?> GetRoleAsync(
        AutoNateDbContext db, Guid projectId, Guid userId, CancellationToken ct)
    {
        var roleString = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);
        return roleString is null ? null : ProjectRoleNames.TryParse(roleString);
    }

    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(
        AutoNateDbContext db, Guid projectId, CancellationToken ct)
    {
        return await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.AddedAtUtc)
            .ToListAsync(ct);
    }

    public Task AddOwnerOnCreateAsync(
        AutoNateDbContext db, Guid projectId, Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = ProjectRoleNames.Owner,
            AddedAtUtc = nowUtc,
            AddedBy = userId,
            UpdatedAtUtc = nowUtc,
            UpdatedBy = userId
        });
        return Task.CompletedTask;
    }

    public async Task SetRoleAsync(
        AutoNateDbContext db, Guid projectId, Guid userId, ProjectRole role,
        Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        var existing = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

        var wire = ProjectRoleNames.ToWire(role);

        if (existing is null)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                UserId = userId,
                Role = wire,
                AddedAtUtc = nowUtc,
                AddedBy = actorId,
                UpdatedAtUtc = nowUtc,
                UpdatedBy = actorId
            });
            return;
        }

        // Demoting the last owner would leave a project ownerless. Reject
        // before mutating so the caller can return a 409 cleanly.
        if (existing.Role == ProjectRoleNames.Owner && role != ProjectRole.Owner)
        {
            await EnsureOtherOwnerExistsAsync(db, projectId, userId, ct);
        }

        existing.Role = wire;
        existing.UpdatedAtUtc = nowUtc;
        existing.UpdatedBy = actorId;
    }

    public async Task RemoveMemberAsync(
        AutoNateDbContext db, Guid projectId, Guid userId, CancellationToken ct)
    {
        var existing = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
        if (existing is null) return;

        if (existing.Role == ProjectRoleNames.Owner)
        {
            await EnsureOtherOwnerExistsAsync(db, projectId, userId, ct);
        }

        db.ProjectMembers.Remove(existing);
    }

    private static async Task EnsureOtherOwnerExistsAsync(
        AutoNateDbContext db, Guid projectId, Guid excludingUserId, CancellationToken ct)
    {
        var otherOwners = await db.ProjectMembers.AsNoTracking()
            .CountAsync(m =>
                m.ProjectId == projectId &&
                m.UserId != excludingUserId &&
                m.Role == ProjectRoleNames.Owner, ct);
        if (otherOwners == 0)
        {
            throw new InvalidOperationException(
                "Cannot remove or demote the last owner of a project.");
        }
    }
}
