using AutoNate.Web.Models.Records;

namespace AutoNate.Web.Services.Records;

public sealed class RecordCommentNotFoundException : Exception
{
    public RecordCommentNotFoundException(Guid id) : base($"Comment '{id}' was not found.") { }
}

public sealed class RecordCommentValidationException : Exception
{
    public RecordCommentValidationException(string message) : base(message) { }
}

public interface IRecordCommentStore
{
    Task<IReadOnlyList<RecordComment>> ListForRecordAsync(
        Guid recordId,
        bool includeDeleted,
        CancellationToken cancellationToken = default);

    Task<RecordComment?> GetAsync(Guid commentId, CancellationToken cancellationToken = default);

    Task<RecordComment> CreateAsync(
        Guid recordId,
        string body,
        Guid authorId,
        CancellationToken cancellationToken = default);

    Task<RecordComment> EditAsync(
        Guid commentId,
        string newBody,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<RecordComment> SoftDeleteAsync(
        Guid commentId,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordCommentRevision>> ListRevisionsAsync(
        Guid commentId,
        CancellationToken cancellationToken = default);
}
