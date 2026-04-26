using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class PermissionGrant
{
    public Guid Id { get; set; }

    public string PrincipalKind { get; set; } = null!;

    public string PrincipalId { get; set; } = null!;

    public string Action { get; set; } = null!;

    public string SelectorString { get; set; } = null!;

    public string SelectorAst { get; set; } = "{}";

    public string Effect { get; set; } = "allow";

    public int Priority { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
