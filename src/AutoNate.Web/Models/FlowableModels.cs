namespace AutoNate.Web.Models;

public sealed record class FlowableProcessDefinitionSummary
{
    public string Id { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Version { get; init; }

    public string DeploymentId { get; init; } = string.Empty;
}

public sealed record class FlowableProcessInstanceSummary
{
    public string Id { get; init; } = string.Empty;

    public string ProcessDefinitionId { get; init; } = string.Empty;

    public string? ActivityId { get; init; }

    public bool Suspended { get; init; }
}

public sealed record class FlowableTaskSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Assignee { get; init; }

    public string? ProcessInstanceId { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }
}
