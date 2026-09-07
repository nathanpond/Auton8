using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Transformers.Code;

public static class CodeTransformerKinds
{
    public const string Transformer = "transformer";
    public const string Analyzer = "analyzer";
}

public static class CodeTransformerLanguages
{
    public const string JavaScript = "js";
    public const string Python = "python";
}

public sealed record class CreateCodeTransformerInput(
    string Name,
    string? Description,
    string Kind,
    string Language,
    string Code);

public sealed record class UpdateCodeTransformerInput(
    string? Name,
    string? Description,
    string? Code);

public sealed class CodeTransformerNotFoundException(Guid id)
    : Exception($"Code transformer '{id}' was not found.");

public sealed class CodeTransformerNameConflictException(string name)
    : Exception($"A code transformer named '{name}' already exists.");

public interface ICodeTransformerStore
{
    Task<IReadOnlyList<CodeTransformer>> ListAsync(CancellationToken cancellationToken = default);

    Task<CodeTransformer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CodeTransformer?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<CodeTransformer> CreateAsync(
        CreateCodeTransformerInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<CodeTransformer> UpdateAsync(
        Guid id,
        UpdateCodeTransformerInput input,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
