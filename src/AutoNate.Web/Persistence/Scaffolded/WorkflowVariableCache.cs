namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowVariableCache
{
    public string FlowableInstanceId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? ValueText { get; set; }

    public long? ValueLong { get; set; }

    public double? ValueDouble { get; set; }

    public bool? ValueBool { get; set; }

    public string? ValueJson { get; set; }

    public string Type { get; set; } = null!;

    public DateTime UpdatedTime { get; set; }

    public int ProjectionVersion { get; set; }

    public DateTime LastSyncAtUtc { get; set; }
}
