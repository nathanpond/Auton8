using System.Text.Json;

namespace AutoNate.Web.Models.Menus;

public sealed record class MenuItem
{
    public Guid Id { get; init; }

    public Guid MenuId { get; init; }

    public Guid? ParentId { get; init; }

    public int SortOrder { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public string ItemType { get; init; } = "group";

    public JsonElement Config { get; init; }

    public string? PermissionRequired { get; init; }

    public bool IsVisible { get; init; }

    public bool IsSystem { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public IReadOnlyList<MenuItem> Children { get; init; } = Array.Empty<MenuItem>();
}
