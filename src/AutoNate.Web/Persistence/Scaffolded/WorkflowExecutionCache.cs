namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowExecutionCache
{
    public string FlowableInstanceId { get; set; } = null!;

    public string ProcessDefinitionKey { get; set; } = null!;

    public string ProcessDefinitionId { get; set; } = null!;

    public int? ProcessDefinitionVersion { get; set; }

    public string? BusinessKey { get; set; }

    public string? TenantId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public long? DurationMs { get; set; }

    public string? StartedBy { get; set; }

    public string? CurrentActivityId { get; set; }

    public string? CurrentActivityName { get; set; }

    // #583. The run's display name (Flowable's act_hi_procinst.name_) and the
    // name of the model it was started from. Null on a projection_version 1 row
    // means "not re-projected yet"; on a version 2 row it means the run has no
    // name in Flowable.
    public string? Name { get; set; }

    public string? WorkflowModelName { get; set; }

    public long? RecordId { get; set; }

    public string AuthTagsJson { get; set; } = "{}";

    public int ProjectionVersion { get; set; }

    public DateTime LastSyncAtUtc { get; set; }
}
