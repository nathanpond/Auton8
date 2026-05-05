using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class FormVersion
{
    public Guid Id { get; set; }

    public Guid FormId { get; set; }

    public int VersionNumber { get; set; }

    public string Name { get; set; } = null!;

    public string ShortCode { get; set; } = null!;

    public string FormCode { get; set; } = null!;

    public bool SiteAvailable { get; set; }

    public string Kind { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
