using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Page
{
    public Guid Id { get; set; }

    public long Locator { get; set; }

    public Guid NotebookId { get; set; }

    public Guid? ParentPageId { get; set; }

    public string Title { get; set; } = null!;

    public string BodyJsonb { get; set; } = null!;

    public int CurrentVersionNumber { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
