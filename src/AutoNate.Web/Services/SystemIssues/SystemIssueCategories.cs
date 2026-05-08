namespace AutoNate.Web.Services.SystemIssues;

// Stable string vocabulary for system_issues.category. Mirrors the
// NotificationKinds pattern: detectors must use one of these constants so the
// SPA filter UI and audit-event consumers see a closed set.
public static class SystemIssueCategories
{
    public const string DataIntegrity = "data_integrity";
    public const string Workflow = "workflow";
    public const string Bus = "bus";
    public const string Auth = "auth";
    public const string Config = "config";
    public const string Resource = "resource";
    public const string Plugin = "plugin";
    public const string Unhandled = "unhandled";
}
