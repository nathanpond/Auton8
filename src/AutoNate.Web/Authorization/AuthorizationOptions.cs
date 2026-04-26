namespace AutoNate.Web.Authorization;

public sealed class AuthorizationOptions
{
    public const string SectionName = "Authorization";

    public bool Enabled { get; set; } = false;

    public string Enforcement { get; set; } = AuthorizationEnforcement.Off;

    public bool AssignSuperAdminToAllExistingUsers { get; set; } = true;

    // When true and Enforcement is Full, write-path AuthorizeAsync logs would-be
    // denials at WARN level but returns Allow. Used as a 24-hour safety window
    // before flipping the lockdown switch in production.
    public bool DryRun { get; set; } = false;
}

public static class AuthorizationEnforcement
{
    public const string Off = "off";
    public const string ReadOnly = "read-only";
    public const string Full = "full";
}
