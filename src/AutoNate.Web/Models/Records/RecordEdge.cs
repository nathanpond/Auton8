using System.Text.Json;

namespace AutoNate.Web.Models.Records;

public sealed record class RecordEdge
{
    public Guid Id { get; init; }

    public Guid EdgeTypeId { get; init; }

    public Guid FromRecordId { get; init; }

    public Guid ToRecordId { get; init; }

    public JsonElement Data { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }
}
