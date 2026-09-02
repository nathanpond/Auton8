namespace AutoNate.Web.Persistence.Scaffolded;

// Per-node row recorded by the orchestrator as it walks the topologically-
// sorted graph. `NodeKey` matches the node id in the run's graph snapshot.
// Status mirrors PipelineRun.Status but is independent per step so a
// downstream failure doesn't retroactively rewrite upstream Succeeded
// rows.
public partial class PipelineRunStep
{
    public Guid Id { get; set; }

    public Guid PipelineRunId { get; set; }

    public string NodeKey { get; set; } = null!;

    public string NodeKind { get; set; } = null!;

    public string Status { get; set; } = "Queued";

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public long? RowCount { get; set; }

    public string? ErrorMessage { get; set; }

    // JSONB array of step log entries (audit fix archived-11). The orchestrator
    // accumulates entries during execution and writes them on step
    // completion; on a fresh row the EF default `"[]"` matches the DB
    // default so an unread step is safely empty.
    public string LogsJson { get; set; } = "[]";
}
