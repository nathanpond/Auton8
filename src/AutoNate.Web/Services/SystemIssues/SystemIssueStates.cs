namespace AutoNate.Web.Services.SystemIssues;

// Lifecycle states. The partial unique index on (fingerprint) covers
// {open, acknowledged} so re-detection during those states is a dedup-update
// rather than a new row. Once an issue moves to {auto_resolved, resolved}, a
// fresh occurrence opens a new row.
public static class SystemIssueStates
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string AutoResolved = "auto_resolved";
    public const string Resolved = "resolved";
}

// How an issue stopped being open.
public static class SystemIssueResolutionKinds
{
    // The remediator dispatcher ran an IIssueRemediator successfully.
    public const string AutoRemediated = "auto_remediated";

    // A human hit the resolve endpoint.
    public const string Manual = "manual";

    // The detector that opened the issue ran again and the underlying
    // condition was gone (e.g. SystemHealthSnapshotDetector found Postgres
    // back up). The detector calls MarkResolvedAsync explicitly.
    public const string NoLongerPresent = "no_longer_present";
}
