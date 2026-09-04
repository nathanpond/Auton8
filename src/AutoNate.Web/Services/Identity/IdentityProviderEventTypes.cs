namespace AutoNate.Web.Services.Identity;

/// <summary>
/// Audit topic for identity-provider configuration.
/// </summary>
/// <remarks>
/// Changing who can sign in is the most audit-worthy mutation the product has,
/// which is why every mutation here emits — including enable and disable, which
/// are the two that change the security posture without changing any field a
/// diff would show.
///
/// The secret never appears in a payload. Only its fingerprint does, and only
/// where identifying *which* secret is in play matters.
/// </remarks>
public static class IdentityProviderEventTopic
{
    public const string TopicRoot = "identity-providers";
    public const string TopicName = "identity-providers.events";
    public const string ResourceKind = "identity-provider";
}

public static class IdentityProviderEventTypes
{
    public const string Created = "identity_provider.created";
    public const string Updated = "identity_provider.updated";
    public const string Deleted = "identity_provider.deleted";
    public const string Enabled = "identity_provider.enabled";
    public const string Disabled = "identity_provider.disabled";
    public const string ConfigurationTested = "identity_provider.configuration_tested";
}
