using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Project
{
    public Guid Id { get; set; }

    public long Locator { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool DeletionsLocked { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
