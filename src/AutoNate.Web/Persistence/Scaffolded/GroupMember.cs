using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class GroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    public DateTime AddedAtUtc { get; set; }

    public Guid AddedBy { get; set; }
}
