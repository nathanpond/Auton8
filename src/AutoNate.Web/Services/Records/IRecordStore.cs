using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records.Fields;

namespace AutoNate.Web.Services.Records;

public sealed record class CreateRecordInput(
    Guid RecordTypeId,
    string Name,
    JsonElement Values,
    IReadOnlyList<Guid>? AssigneeIds);

public sealed record class UpdateRecordInput(
    string? Name,
    JsonElement? Values,
    IReadOnlyList<Guid>? AssigneeIds);

public sealed record class RecordListPage(
    IReadOnlyList<Record> Records,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record class RecordFilterClause(string FieldKey, FilterOperator Operator, JsonElement Value);

public sealed record class RecordSearchInput(
    Guid RecordTypeId,
    IReadOnlyList<RecordFilterClause>? Filters,
    Guid? AssigneeId,
    bool IncludeArchived,
    int Page,
    int PageSize,
    string? Sort);

public sealed class RecordValidationException : Exception
{
    public IReadOnlyList<FieldValidationError> Errors { get; }

    public RecordValidationException(string message, IReadOnlyList<FieldValidationError>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<FieldValidationError>();
    }
}

public sealed class RecordNotFoundException : Exception
{
    public RecordNotFoundException(Guid id) : base($"Record '{id}' was not found.") { }
}

public interface IRecordStore
{
    Task<RecordListPage> SearchAsync(RecordSearchInput input, CancellationToken cancellationToken = default);

    Task<RecordListPage> SearchAssignedAsync(
        Guid assigneeId,
        int page,
        int pageSize,
        bool includeArchived,
        string? sort,
        CancellationToken cancellationToken = default);

    Task<Record?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Record?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<Record> CreateAsync(CreateRecordInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<Record> UpdateAsync(Guid id, UpdateRecordInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<Record> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default);
}

public interface IRecordHistoryStore
{
    Task<IReadOnlyList<RecordFieldChange>> ListAsync(
        Guid recordId,
        string? fieldKey,
        int take,
        CancellationToken cancellationToken = default);
}
