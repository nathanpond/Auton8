namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowTaskCache
{
    public string FlowableTaskId { get; set; } = null!;

    public string FlowableInstanceId { get; set; } = null!;

    public string ProcessDefinitionKey { get; set; } = null!;

    public string? TaskDefinitionKey { get; set; }

    public string? Name { get; set; }

    public string? Assignee { get; set; }

    public string? Owner { get; set; }

    public string[] CandidateUsers { get; set; } = Array.Empty<string>();

    public string[] CandidateGroups { get; set; } = Array.Empty<string>();

    public DateTime? DueDate { get; set; }

    public DateTime CreatedTime { get; set; }

    public DateTime? ClaimTime { get; set; }

    public DateTime? CompletedTime { get; set; }

    public string? FormKey { get; set; }

    public int? Priority { get; set; }

    public string Status { get; set; } = null!;

    public string AuthTagsJson { get; set; } = "{}";

    public int ProjectionVersion { get; set; }

    public DateTime LastSyncAtUtc { get; set; }
}
