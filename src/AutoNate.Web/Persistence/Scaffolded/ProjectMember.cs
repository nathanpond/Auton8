using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class ProjectMember
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    // 'owner' | 'contributor' | 'commenter' | 'viewer'
    public string Role { get; set; } = null!;

    public DateTime AddedAtUtc { get; set; }

    public Guid AddedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
