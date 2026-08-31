using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Datasets;

public sealed record class CreateDatasetInput(
    string Name,
    string? Description,
    DatasetMode Mode,
    IReadOnlyList<DatasetColumn> Columns,
    string SourceKind,
    Guid SourceId,
    string? SourceTableName,
    string? RefreshCron,
    // Files-datastore scope. SourceTableName stays null for these; the
    // FileScopeKind ("file" | "folder") + FileScopePath pair selects the
    // single file or single folder backing the dataset. ParserKind +
    // ParserOptionsJson are required alongside the scope so the executor
    // and materializer can parse file contents into the locked column
    // schema. All four stay null for SQL-datastore / connector sources.
    string? FileScopeKind = null,
    string? FileScopePath = null,
    string? ParserKind = null,
    string? ParserOptionsJson = null);

public sealed record class UpdateDatasetInput(
    string? Name,
    string? Description,
    string? RefreshCron);

public sealed class DatasetNotFoundException(Guid id)
    : Exception($"Dataset '{id}' was not found.");

public sealed class DatasetNameConflictException(string name)
    : Exception($"A dataset named '{name}' already exists.");

public interface IDatasetStore
{
    Task<IReadOnlyList<Dataset>> ListAsync(CancellationToken cancellationToken = default);

    Task<Dataset?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Dataset?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<Dataset> CreateAsync(
        CreateDatasetInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<Dataset> UpdateAsync(
        Guid id,
        UpdateDatasetInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Bump after a refresh so the SPA's "last refreshed" column reflects
    // reality. Only the DatasetRefreshProjection should call this on
    // success; manual /refresh enqueues a projection tick and lets that
    // path update.
    Task MarkRefreshedAsync(
        Guid id,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
