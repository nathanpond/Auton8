namespace AutoNate.Web.Services.Identity;

/// <summary>
/// Audit event types for identity-provider claim mapping (#92).
/// </summary>
/// <remarks>
/// On the <c>iam.events</c> topic with the rest of access administration, not a
/// topic of their own. A mapping is an access-control rule: someone auditing
/// "who has what access today and who granted it" has to see these alongside
/// manual grants, or federation becomes a blind spot in exactly the report that
/// exists to have none.
/// </remarks>
public static class IdentityEventTypes
{
    public const string GroupMappingCreated = "iam.identity-provider.group-mapping.created";
    public const string GroupMappingUpdated = "iam.identity-provider.group-mapping.updated";
    public const string GroupMappingDeleted = "iam.identity-provider.group-mapping.deleted";

    /// <summary>A sign-in reconciliation granted a group from a claim.</summary>
    public const string ClaimGroupGranted = "iam.identity-provider.claim-group.granted";

    /// <summary>A sign-in reconciliation revoked a group whose claim had gone.</summary>
    public const string ClaimGroupRevoked = "iam.identity-provider.claim-group.revoked";
}
