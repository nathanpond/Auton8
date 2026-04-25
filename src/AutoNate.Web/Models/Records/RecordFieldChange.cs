using System.Text.Json;

namespace AutoNate.Web.Models.Records;

public sealed record class RecordFieldChange
{
    public long Id { get; init; }

    public Guid RecordId { get; init; }

    public Guid? ChangeSetId { get; init; }

    public string ChangeKind { get; init; } = string.Empty;

    public string? FieldKey { get; init; }

    public JsonElement? OldValue { get; init; }

    public JsonElement? NewValue { get; init; }

    public Guid ChangedBy { get; init; }

    public DateTimeOffset ChangedAtUtc { get; init; }
}

public static class RecordChangeKinds
{
    public const string Created = "created";
    public const string ValueChanged = "value_changed";
    public const string NameChanged = "name_changed";
    public const string AssigneesChanged = "assignees_changed";
    public const string Archived = "archived";
    public const string Unarchived = "unarchived";
}
