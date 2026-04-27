using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class PageTemplate
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string DefaultPath { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
