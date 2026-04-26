using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class StatusAppearanceEntry
{
    public Guid Id { get; set; }

    public string Status { get; set; } = null!;

    public string Color { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
