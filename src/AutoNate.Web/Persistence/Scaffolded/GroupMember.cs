using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class GroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    public DateTime AddedAtUtc { get; set; }

    public Guid AddedBy { get; set; }

    /// <summary>One of <see cref="Models.GroupMembershipSources"/> (#92).</summary>
    /// <remarks>
    /// Defaults to <c>manual</c> in the schema, which is a statement of fact
    /// rather than a convenience: every row that existed before claim mapping
    /// was put there by a person.
    /// </remarks>
    public string Source { get; set; } = Models.GroupMembershipSources.Manual;

    /// <summary>
    /// Which identity provider owns this row, when <see cref="Source"/> is
    /// <c>idp</c>; null otherwise.
    /// </summary>
    /// <remarks>
    /// Two providers configured against one Auton8 must not be able to revoke
    /// each other's grants, so reconciliation is scoped to the provider the
    /// user actually signed in through.
    /// </remarks>
    public Guid? SourceProviderId { get; set; }
}
