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
}
