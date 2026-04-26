using System.Security.Claims;
using AutoNate.Web.Models.Authorization;

namespace AutoNate.Web.Services.Authorization;

public sealed record class CreateGroupInput(string Name, string? Description);

public sealed record class UpdateGroupInput(string? Name, string? Description);

public sealed class GroupNotFoundException(Guid id) : Exception($"Group '{id}' was not found.");

public sealed class GroupValidationException(string message) : Exception(message);

public interface IGroupStore
{
    Task<IReadOnlyList<Group>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Group>> ListAuthorizedAsync(
        ClaimsPrincipal actor,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    Task<Group?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Group> CreateAsync(CreateGroupInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<Group> UpdateAsync(Guid id, UpdateGroupInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<Group> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupMember>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Group>> ListGroupsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> AddMemberAsync(Guid groupId, Guid userId, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
}
