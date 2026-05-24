namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowEventLogCache
{
    public string EventId { get; set; } = null!;

    public string FlowableInstanceId { get; set; } = null!;

    public string ProcessDefinitionKey { get; set; } = null!;

    public DateTime EventTime { get; set; }

    public string EventType { get; set; } = null!;

    public string? ActivityId { get; set; }

    public string? ActivityName { get; set; }

    public string? ActivityType { get; set; }

    public string? TaskId { get; set; }

    public string? VariableName { get; set; }

    public string? Actor { get; set; }

    public long? DurationMs { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public int ProjectionVersion { get; set; }

    public DateTime LastSyncAtUtc { get; set; }
}
