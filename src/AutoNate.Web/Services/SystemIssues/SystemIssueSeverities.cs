namespace AutoNate.Web.Services.SystemIssues;

// Severity ladder. `info` is benign / FYI; `critical` should also publish an
// in-app notification (Phase 3). Detectors are free to bump the severity of
// an existing issue on subsequent ticks (severity escalation is recorded as a
// separate audit event in Phase 3).
public static class SystemIssueSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}
