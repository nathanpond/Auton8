namespace AutoNate.Web.Persistence.Scaffolded;

// One execution of a pipeline. Lifecycle: Queued → Running → (Succeeded |
// Failed | Cancelled). The graph snapshot is captured on enqueue so a
// concurrent edit to the parent pipeline can't change a run mid-flight.
public partial class PipelineRun
{
    public Guid Id { get; set; }

    public Guid PipelineId { get; set; }

    // "Queued" | "Running" | "Succeeded" | "Failed" | "Cancelled"
    public string Status { get; set; } = "Queued";

    // Snapshot of pipelines.graph_json at enqueue time.
    public string GraphSnapshotJson { get; set; } = "{}";

    public DateTime QueuedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid TriggeredBy { get; set; }

    // "manual" | "scheduled"
    public string TriggerKind { get; set; } = "manual";
}
