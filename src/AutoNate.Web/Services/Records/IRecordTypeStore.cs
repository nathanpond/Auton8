using AutoNate.Web.Models.Records;

namespace AutoNate.Web.Services.Records;

public sealed record class CreateRecordTypeInput(
    string ShortCode,
    string Name,
    string? Description,
    string? Icon,
    string? Color);

public sealed record class UpdateRecordTypeInput(
    string Name,
    string? Description,
    string? Icon,
    string? Color);

public sealed record class CreateRecordTypeFieldInput(
    string FieldKey,
    string DisplayName,
    string DataType,
    System.Text.Json.JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record class UpdateRecordTypeFieldInput(
    string DisplayName,
    System.Text.Json.JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed class RecordTypeValidationException : Exception
{
    public RecordTypeValidationException(string message) : base(message) { }
}

public sealed class RecordTypeNotFoundException : Exception
{
    public RecordTypeNotFoundException(Guid id) : base($"Record type '{id}' was not found.") { }
}

public sealed class RecordTypeFieldNotFoundException : Exception
{
    public RecordTypeFieldNotFoundException(Guid id) : base($"Record type field '{id}' was not found.") { }
}

public interface IRecordTypeStore
{
    Task<IReadOnlyList<RecordType>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<RecordType?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RecordType?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    Task<RecordType> CreateAsync(CreateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<RecordType> UpdateAsync(Guid id, UpdateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<RecordType> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordTypeField>> ListFieldsAsync(Guid recordTypeId, bool includeArchived, CancellationToken cancellationToken = default);

    Task<RecordTypeField?> GetFieldAsync(Guid recordTypeId, Guid fieldId, CancellationToken cancellationToken = default);

    Task<RecordTypeField> CreateFieldAsync(Guid recordTypeId, CreateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<RecordTypeField> UpdateFieldAsync(Guid recordTypeId, Guid fieldId, UpdateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<RecordTypeField> SetFieldArchivedAsync(Guid recordTypeId, Guid fieldId, bool archived, Guid actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordTypeAuditEntry>> ListAuditAsync(Guid recordTypeId, int take, CancellationToken cancellationToken = default);
}
