using System.Text.Json;

namespace AutoNate.Web.Models.Records;

public sealed record class Record
{
    public Guid Id { get; init; }

    public Guid RecordTypeId { get; init; }

    public string Key { get; init; } = string.Empty;

    public long KeyNumber { get; init; }

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<Guid> AssigneeIds { get; init; } = Array.Empty<Guid>();

    public string? Status { get; init; }

    public DateOnly? DueDate { get; init; }

    public JsonElement Values { get; init; }

    public bool IsArchived { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid UpdatedBy { get; init; }
}
