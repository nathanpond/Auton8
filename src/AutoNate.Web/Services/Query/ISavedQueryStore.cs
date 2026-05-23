using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Query;

public sealed record class CreateSavedQueryInput(
    string Name,
    string? Description,
    string QueryText,
    bool IsShared);

public sealed record class UpdateSavedQueryInput(
    string? Name,
    string? Description,
    string? QueryText,
    bool? IsShared);

public sealed class SavedQueryNotFoundException(Guid id)
    : Exception($"Saved query '{id}' was not found.");

// Owner uniqueness is enforced by uq_saved_queries_owner_name; the store
// catches the 23505 and surfaces this so the endpoint can map it to 409.
public sealed class SavedQueryNameConflictException(string name)
    : Exception($"A saved query named '{name}' already exists for this user.");

public interface ISavedQueryStore
{
    // Lists every saved query the actor can see: their own rows plus every
    // is_shared row. Ordered by name (case-insensitive) so the SPA combobox
    // has a stable alphabetical list out of the box.
    Task<IReadOnlyList<SavedQuery>> ListForActorAsync(
        Guid actorId,
        CancellationToken cancellationToken = default);

    // Returns null when the row doesn't exist OR when the actor is neither
    // the owner nor allowed via is_shared. The endpoint maps null to 404.
    Task<SavedQuery?> GetForActorAsync(
        Guid id,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<SavedQuery> CreateAsync(
        CreateSavedQueryInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    // Throws SavedQueryNotFoundException when the row is missing AND when
    // the actor is not the owner — endpoint code maps both to 404 so an
    // owner-only row stays invisible to non-owners.
    Task<SavedQuery> UpdateAsync(
        Guid id,
        UpdateSavedQueryInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        Guid actorId,
        CancellationToken cancellationToken = default);
}
