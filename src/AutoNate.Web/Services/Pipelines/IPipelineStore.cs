using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Pipelines;

public sealed record class CreatePipelineInput(
    string Name,
    string? Description,
    PipelineGraph Graph,
    string? ScheduleCron);

public sealed record class UpdatePipelineInput(
    string? Name,
    string? Description,
    PipelineGraph? Graph,
    string? ScheduleCron);

public sealed class PipelineNotFoundException(Guid id)
    : Exception($"Pipeline '{id}' was not found.");

public sealed class PipelineNameConflictException(string name)
    : Exception($"A pipeline named '{name}' already exists.");

public interface IPipelineStore
{
    Task<IReadOnlyList<Pipeline>> ListAsync(CancellationToken cancellationToken = default);

    Task<Pipeline?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Pipeline> CreateAsync(
        CreatePipelineInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<Pipeline> UpdateAsync(
        Guid id,
        UpdatePipelineInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkRunCompletedAsync(
        Guid id,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
