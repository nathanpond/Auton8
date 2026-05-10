using System.Text.Json;
using AutoNate.Web.Models.Records;

namespace AutoNate.Web.Services.Records;

public sealed record class CreateRecordEdgeTypeInput(
    string ShortCode,
    string Name,
    string? InverseName,
    bool IsDirected,
    bool AllowSelfReference,
    string Cardinality,
    IReadOnlyList<Guid>? FromRecordTypeIds,
    IReadOnlyList<Guid>? ToRecordTypeIds);

public sealed record class UpdateRecordEdgeTypeInput(
    string Name,
    string? InverseName,
    bool IsDirected,
    bool AllowSelfReference,
    string Cardinality,
    IReadOnlyList<Guid>? FromRecordTypeIds,
    IReadOnlyList<Guid>? ToRecordTypeIds);

public sealed record class CreateRecordEdgeTypeFieldInput(
    string FieldKey,
    string DisplayName,
    string DataType,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record class UpdateRecordEdgeTypeFieldInput(
    string DisplayName,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record class CreateRecordEdgeInput(
    Guid EdgeTypeId,
    Guid FromRecordId,
    Guid ToRecordId,
    JsonElement Data);

public enum EdgeDirection
{
    Outgoing,
    Incoming,
    Both
}

public sealed record class TraverseRequest(
    IReadOnlyList<Guid> StartRecordIds,
    IReadOnlyList<Guid>? EdgeTypeIds,
    EdgeDirection Direction,
    int MaxHops);

public sealed record class TraverseResultRow(Guid RecordId, int Hops);

public sealed class RecordEdgeTypeNotFoundException : Exception
{
    public RecordEdgeTypeNotFoundException(Guid id) : base($"Record edge type '{id}' was not found.") { }
}

public sealed class RecordEdgeNotFoundException : Exception
{
    public RecordEdgeNotFoundException(Guid id) : base($"Record edge '{id}' was not found.") { }
}

public sealed class RecordEdgeValidationException : Exception
{
    public RecordEdgeValidationException(string message) : base(message) { }
}

public interface IRecordEdgeTypeStore
{
    Task<IReadOnlyList<RecordEdgeType>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<RecordEdgeType?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RecordEdgeType> CreateAsync(CreateRecordEdgeTypeInput input, CancellationToken cancellationToken = default);

    Task<RecordEdgeType> UpdateAsync(Guid id, UpdateRecordEdgeTypeInput input, CancellationToken cancellationToken = default);

    Task<RecordEdgeType> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordEdgeTypeField>> ListFieldsAsync(Guid edgeTypeId, CancellationToken cancellationToken = default);

    Task<RecordEdgeTypeField> CreateFieldAsync(Guid edgeTypeId, CreateRecordEdgeTypeFieldInput input, CancellationToken cancellationToken = default);

    Task<RecordEdgeTypeField> UpdateFieldAsync(Guid edgeTypeId, Guid fieldId, UpdateRecordEdgeTypeFieldInput input, CancellationToken cancellationToken = default);

    Task DeleteFieldAsync(Guid edgeTypeId, Guid fieldId, CancellationToken cancellationToken = default);
}

public interface IRecordEdgeStore
{
    Task<RecordEdge> CreateAsync(CreateRecordEdgeInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<RecordEdge?> GetAsync(Guid edgeId, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid edgeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordEdge>> ListForRecordAsync(
        Guid recordId,
        EdgeDirection direction,
        Guid? edgeTypeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TraverseResultRow>> TraverseAsync(
        TraverseRequest request,
        CancellationToken cancellationToken = default);
}
