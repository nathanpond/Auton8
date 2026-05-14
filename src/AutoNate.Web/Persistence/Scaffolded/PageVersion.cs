using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class PageVersion
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }

    public int VersionNumber { get; set; }

    public string Title { get; set; } = null!;

    public string BodyJsonb { get; set; } = null!;

    // 'autosave' | 'manual' | 'restore'
    public string Kind { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
