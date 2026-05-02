namespace AutoNate.Web.Models.Menus;

public sealed record class PageTemplate
{
    public Guid Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ThumbnailUrl { get; init; }

    public string? Category { get; init; }

    public bool IsEnabled { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
