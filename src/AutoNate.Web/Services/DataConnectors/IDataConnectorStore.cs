using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.DataConnectors;

public sealed record class CreateDataConnectorInput(
    string Name,
    string? Description,
    // String-keyed so plugins can ship new kinds without an enum change. The
    // built-in REST and SMB kinds use the constants in `DataConnectorKinds`.
    string Kind,
    // Connector-specific configuration (REST URL + headers, SMB share path,
    // etc.). Schema is the handler's concern; the store treats it as opaque.
    string ConfigJson);

public sealed record class UpdateDataConnectorInput(
    string? Name,
    string? Description,
    string? ConfigJson);

public sealed class DataConnectorNotFoundException(Guid id)
    : Exception($"Data connector '{id}' was not found.");

public sealed class DataConnectorNameConflictException(string name)
    : Exception($"A data connector named '{name}' already exists.");

public interface IDataConnectorStore
{
    Task<IReadOnlyList<DataConnector>> ListAsync(CancellationToken cancellationToken = default);

    Task<DataConnector?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DataConnector> CreateAsync(
        CreateDataConnectorInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<DataConnector> UpdateAsync(
        Guid id,
        UpdateDataConnectorInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
