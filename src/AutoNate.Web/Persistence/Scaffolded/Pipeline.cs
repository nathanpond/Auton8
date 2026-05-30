namespace AutoNate.Web.Persistence.Scaffolded;

// Analytics pipeline definition (Phase 5 of the Data Stores plan). The DAG
// itself lives in `GraphJson` — nodes (dataset-source / transformer /
// analyzer / dataset-sink), edges, per-node config. Runs created from this
// pipeline draw a snapshot of the graph so an edit-in-flight doesn't
// retro-change a queued or in-progress run.
public partial class Pipeline
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string GraphJson { get; set; } = "{\"nodes\":[],\"edges\":[]}";

    // Cron expression for scheduled runs. NULL = manual-only. v1 cron parser
    // matches the dataset-refresh scheduler's `*/N * * * *` minute form;
    // richer cron lands when projection framework integration arrives.
    public string? ScheduleCron { get; set; }

    public DateTime? LastRunAtUtc { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
