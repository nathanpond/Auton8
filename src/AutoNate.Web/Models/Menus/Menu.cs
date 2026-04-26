namespace AutoNate.Web.Models.Menus;

public sealed record class Menu
{
    public Guid Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsSystem { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid UpdatedBy { get; init; }

    public IReadOnlyList<MenuItem> Items { get; init; } = Array.Empty<MenuItem>();
}
