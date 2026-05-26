using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Folder
{
    public Guid Id { get; set; }

    public long Locator { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? ParentFolderId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
