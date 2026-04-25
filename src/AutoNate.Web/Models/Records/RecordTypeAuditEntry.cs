using System.Text.Json;

namespace AutoNate.Web.Models.Records;

public sealed record class RecordTypeAuditEntry
{
    public long Id { get; init; }

    public Guid RecordTypeId { get; init; }

    public string ChangeKind { get; init; } = string.Empty;

    public JsonElement? Before { get; init; }

    public JsonElement? After { get; init; }

    public Guid ChangedBy { get; init; }

    public DateTimeOffset ChangedAtUtc { get; init; }
}

public static class RecordTypeAuditChangeKinds
{
    public const string TypeCreated = "type_created";
    public const string TypeUpdated = "type_updated";
    public const string TypeArchived = "type_archived";
    public const string TypeUnarchived = "type_unarchived";
    public const string FieldAdded = "field_added";
    public const string FieldRenamed = "field_renamed";
    public const string FieldConfigChanged = "field_config_changed";
    public const string FieldRequiredChanged = "field_required_changed";
    public const string FieldReordered = "field_reordered";
    public const string FieldArchived = "field_archived";
    public const string FieldUnarchived = "field_unarchived";
}
