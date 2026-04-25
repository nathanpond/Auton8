using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordType
{
    public Guid Id { get; set; }

    public string ShortCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public string? Color { get; set; }

    public bool IsSystem { get; set; }

    public bool IsArchived { get; set; }

    public long NextKeyNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
