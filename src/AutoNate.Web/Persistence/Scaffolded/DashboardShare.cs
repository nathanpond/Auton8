using System;

namespace AutoNate.Web.Persistence.Scaffolded;

// v1 future-proofing seam: the table exists and is FK-cascaded with
// `dashboards`, but no SPA UI writes to it yet. Once a Share modal is added,
// EfCoreDashboardStore's List() query joins through this table so a user
// sees both owned and shared dashboards.
public partial class DashboardShare
{
    public Guid DashboardId { get; set; }

    // 'user' | 'group' | 'role'
    public string PrincipalType { get; set; } = "user";

    public Guid PrincipalId { get; set; }

    // 'viewer' | 'editor'
    public string Role { get; set; } = "viewer";

    public DateTime GrantedAtUtc { get; set; }

    public Guid GrantedBy { get; set; }
}
