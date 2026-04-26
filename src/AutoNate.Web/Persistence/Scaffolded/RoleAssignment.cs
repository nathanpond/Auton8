using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RoleAssignment
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public string PrincipalKind { get; set; } = null!;

    public string PrincipalId { get; set; } = null!;

    public string? ScopeString { get; set; }

    public string? ScopeAst { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
