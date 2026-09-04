namespace AutoNate.Web.Services.Auth;

// Topic + event-type names for the auth.events bus topic. Phase 2 of the
// audit-events plan introduces this domain — every login attempt, logout,
// permission probe, and authorization denial publishes one event here so an
// audit consumer can answer "who tried what, when, and was it allowed?"
// without joining back to request logs.
public static class AuthEventTopic
{
    public const string TopicRoot = "auth";
    public const string TopicName = "auth.events";
    public const string ResourceKind = "auth";
}

public static class AuthEventTypes
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string AccountLocked = "auth.account.locked";
    public const string AccountUnlocked = "auth.account.unlocked";
    public const string Logout = "auth.logout";
    public const string MeViewed = "auth.me.viewed";
    public const string PermissionChecked = "auth.permission.checked";
    public const string AccessDenied = "auth.access.denied";

    /// <summary>An administrator changed which sign-in methods are enabled (#94).</summary>
    public const string SignInMethodsChanged = "auth.signin-methods.changed";

    /// <summary>
    /// The break-glass override was active at startup (#94).
    /// </summary>
    /// <remarks>
    /// Audited, not merely logged. An operator forcing local sign-in back on
    /// after a bad configuration is exactly the event an incident review goes
    /// looking for, and it should be findable in the same place as the sign-ins
    /// it made possible rather than only in a log file that may have rotated.
    /// </remarks>
    public const string LocalSignInForcedOn = "auth.signin-methods.forced-local";
}
