namespace AutoNate.Web.Models.Authorization;

public sealed record class Role
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsSystem { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid UpdatedBy { get; init; }
}
