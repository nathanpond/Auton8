namespace AutoNate.Web.Services.SystemIssues;

// Read surface for the SPA admin page and the remediation dispatcher.
// Recording is on ISystemIssueRecorder (split so detectors don't take a
// dependency on read APIs they shouldn't use). EfCoreSystemIssueStore
// implements both.
public interface ISystemIssueStore
{
    Task<IReadOnlyList<SystemIssue>> ListAsync(SystemIssueListQuery query, CancellationToken cancellationToken = default);

    Task<SystemIssue?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // Returns the fingerprints of every open or acknowledged issue currently
    // attributed to the given detector. Used by detectors that need to know
    // "which of my issues should auto-resolve because the underlying
    // condition cleared this tick" — necessary because in-memory tracking
    // is lost on app restart, leaving previously-opened issues stranded.
    Task<IReadOnlyList<string>> ListOpenFingerprintsForDetectorAsync(
        string detectorId,
        CancellationToken cancellationToken = default);
}

public sealed record SystemIssueListQuery(
    string? State = SystemIssueStates.Open,
    string? Severity = null,
    string? Category = null,
    int Skip = 0,
    int Take = 100);
