namespace AutoNate.Web.Models;

/// <summary>
/// How a membership row came to exist.
/// </summary>
/// <remarks>
/// Reconciliation removes memberships whose claim has disappeared, so it has to
/// know which rows are its to remove. Without this distinction the first claim
/// to go missing would take an administrator's manual grant with it, and the
/// person who lost access would have no way to tell why.
/// </remarks>
public static class GroupMembershipSources
{
    /// <summary>A person put this row here. Reconciliation never touches it.</summary>
    public const string Manual = "manual";

    /// <summary>An identity provider claim put this row here, and may take it away.</summary>
    public const string Idp = "idp";
}

/// <summary>
/// One edge: "this claim value, from this provider, grants this group".
/// </summary>
/// <remarks>
/// The mapping is the whole gate. A group created in the identity provider has
/// no effect in Auton8 until someone here decides it should — which is what
/// stops federation becoming a second bulk-grant path, where anyone who can
/// create a group at the IdP can grant themselves access here.
///
/// It grants a <em>group</em> and never a role, deliberately: groups already
/// hold role assignments, so the group → role path stays the single place
/// authorization is reasoned about.
/// </remarks>
public sealed class IdentityProviderGroupMappingModel
{
    public Guid Id { get; set; }

    public Guid ProviderId { get; set; }

    /// <summary>
    /// The claim type, exactly as it arrives — e.g. <c>groups</c> for OIDC, or
    /// the full attribute URI for SAML.
    /// </summary>
    public string ClaimType { get; set; } = null!;

    /// <summary>
    /// The claim value, matched exactly.
    /// </summary>
    /// <remarks>
    /// Exact, not a pattern. A wildcard on an authorization path is one typo
    /// away from granting everything, and the blast radius of that typo is
    /// every group in the install.
    /// </remarks>
    public string ClaimValue { get; set; } = null!;

    public Guid GroupId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedBy { get; set; }
}
