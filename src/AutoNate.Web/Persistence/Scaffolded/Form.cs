using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Form
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string ShortCode { get; set; } = null!;

    public string FormCode { get; set; } = null!;

    public bool SiteAvailable { get; set; }

    public bool IsDraft { get; set; }

    public int DraftVersionNumber { get; set; }

    public int? PublishedVersionNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
