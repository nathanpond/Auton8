using System.Text.Json;

namespace AutoNate.Web.Models.Records;

public sealed record class RecordEdgeTypeField
{
    public Guid Id { get; init; }

    public Guid EdgeTypeId { get; init; }

    public string FieldKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public JsonElement Config { get; init; }

    public bool IsRequired { get; init; }

    public int SortOrder { get; init; }
}
