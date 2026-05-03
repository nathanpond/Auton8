namespace AutoNate.Web.Services.SystemIssues;

// One remediator per detector that knows how to safely fix an issue. The
// dispatcher polls open issues with `next_remediation_after_utc <= NOW()` and
// invokes the remediator that claims the matching detector_id (or
// fingerprint-prefix). Returning Success marks the issue auto_resolved;
// Failure bumps the attempt count with exponential backoff up to MaxAttempts.
public interface IIssueRemediator
{
    // Stable identifier; usually equals the detector_id whose issues this
    // remediator handles. The dispatcher matches issues to remediators on this.
    string DetectorId { get; }

    // Optional fingerprint-prefix filter when one detector emits multiple
    // orphan classes through one DetectorId (e.g. OrphanReferenceDetector).
    // Return true to claim. Default = match-any.
    bool CanRemediate(SystemIssue issue) => true;

    Task<RemediationResult> TryRemediateAsync(SystemIssue issue, CancellationToken cancellationToken);
}

public abstract record RemediationResult
{
    public sealed record Success(string? Notes = null) : RemediationResult;

    public sealed record Failure(string Error) : RemediationResult;

    // Remediator looked at the issue, decided this isn't its job after all
    // (e.g. fingerprint prefix didn't really match). Doesn't consume an
    // attempt — dispatcher just clears next_remediation_after_utc so it stops
    // polling this row.
    public sealed record Skip(string Reason) : RemediationResult;
}
