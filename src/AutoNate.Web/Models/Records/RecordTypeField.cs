using System.Text.Json;

namespace AutoNate.Web.Models.Records;

public sealed record class RecordTypeField
{
    public Guid Id { get; init; }

    public Guid RecordTypeId { get; init; }

    public string FieldKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public JsonElement Config { get; init; }

    public bool IsRequired { get; init; }

    public bool IsArchived { get; init; }

    public int SortOrder { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
