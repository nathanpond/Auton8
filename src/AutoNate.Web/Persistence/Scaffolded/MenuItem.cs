using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class MenuItem
{
    public Guid Id { get; set; }

    public Guid MenuId { get; set; }

    public Guid? ParentId { get; set; }

    public int SortOrder { get; set; }

    public string DisplayName { get; set; } = null!;

    public string? Icon { get; set; }

    public string ItemType { get; set; } = null!;

    public string Config { get; set; } = "{}";

    public string? PermissionRequired { get; set; }

    public bool IsVisible { get; set; }

    public bool IsSystem { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
