namespace AutoNate.Web.Models.Records;

public sealed record class RecordType
{
    public Guid Id { get; init; }

    public string ShortCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Icon { get; init; }

    public string? Color { get; init; }

    public bool IsSystem { get; init; }

    public bool IsArchived { get; init; }

    public long NextKeyNumber { get; init; } = 1;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid UpdatedBy { get; init; }
}
