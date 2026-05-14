using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Content;

public interface IProjectMembershipService
{
    Task<ProjectRole?> GetRoleAsync(AutoNateDbContext db, Guid projectId, Guid userId, CancellationToken ct);

    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(AutoNateDbContext db, Guid projectId, CancellationToken ct);

    // Inserts the row inside the caller's tx. Does not call SaveChanges.
    Task AddOwnerOnCreateAsync(AutoNateDbContext db, Guid projectId, Guid userId, DateTime nowUtc, CancellationToken ct);

    // Upserts a user's role on a project. Caller is responsible for the
    // outer transaction. Throws InvalidOperationException if demoting the
    // last owner.
    Task SetRoleAsync(AutoNateDbContext db, Guid projectId, Guid userId, ProjectRole role, Guid actorId, DateTime nowUtc, CancellationToken ct);

    // Removes a member. Throws InvalidOperationException if removing the
    // last owner. Caller owns the outer transaction.
    Task RemoveMemberAsync(AutoNateDbContext db, Guid projectId, Guid userId, CancellationToken ct);
}
