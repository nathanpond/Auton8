namespace AutoNate.Web.Persistence.Scaffolded;

// Self-healing platform: every detector writes one row per distinct issue it
// finds. Dedup is enforced by a partial unique index on fingerprint where
// state IN ('open', 'acknowledged') — re-detecting an open issue bumps
// occurrence_count instead of inserting a new row. See
// EfCoreSystemIssueStore for the upsert that exploits the index.
public partial class SystemIssue
{
    public Guid Id { get; set; }

    public string DetectorId { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string Fingerprint { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Summary { get; set; }

    public string? RelatedEntityKind { get; set; }

    public string? RelatedEntityId { get; set; }

    // Detector-specific structured payload — IDs, counts, error messages.
    // Never row payloads (PII discipline mirrors EventCatalog).
    public string FactsJson { get; set; } = "{}";

    public string State { get; set; } = "open";

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public int OccurrenceCount { get; set; }

    public DateTime? AcknowledgedAtUtc { get; set; }

    public Guid? AcknowledgedBy { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public string? ResolutionKind { get; set; }

    public string? ResolutionNotes { get; set; }

    public int AutoRemediationAttemptCount { get; set; }

    public string? AutoRemediationLastError { get; set; }

    // Set by the dispatcher on retry, or set initially by a detector that
    // wants the remediator to pick this up. NULL means "no remediator
    // registered or remediator already given up".
    public DateTime? NextRemediationAfterUtc { get; set; }
}
