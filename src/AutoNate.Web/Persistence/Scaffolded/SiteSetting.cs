using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class SiteSetting
{
    public string Key { get; set; } = null!;

    public string ValueJson { get; set; } = null!;

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
