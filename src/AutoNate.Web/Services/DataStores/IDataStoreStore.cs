using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.DataStores;

public sealed record class CreateDataStoreInput(
    string Name,
    string? Description,
    DataStoreKind Kind);

public sealed record class UpdateDataStoreInput(
    string? Name,
    string? Description);

public sealed class DataStoreNotFoundException(Guid id)
    : Exception($"Data store '{id}' was not found.");

// `datastores.name` is globally unique (case-insensitive) so the AQL
// `Dataset(...)` lookups landing in Phase 2 have a single, stable handle.
public sealed class DataStoreNameConflictException(string name)
    : Exception($"A data store named '{name}' already exists.");

// CRUD over the datastore metadata table in the primary DB. Per-kind
// provisioning (file root, sql schema + role) is layered in the kind
// handlers; this store owns identity only.
public interface IDataStoreStore
{
    Task<IReadOnlyList<DataStore>> ListAsync(CancellationToken cancellationToken = default);

    Task<DataStore?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DataStore> CreateAsync(
        CreateDataStoreInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<DataStore> UpdateAsync(
        Guid id,
        UpdateDataStoreInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
