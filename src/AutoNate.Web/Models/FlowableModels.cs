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

public sealed record class FlowableHistoricProcessInstanceSummary
{
    public string Id { get; init; } = string.Empty;

    public string ProcessDefinitionId { get; init; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }
}

public sealed record class FlowableTaskSummary
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Assignee { get; init; }

    public string? ProcessInstanceId { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record class WorkflowExecutionSummary
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? CurrentStep { get; init; }
}

public sealed record class WorkflowExecutionDiagramDetail
{
    public string ExecutionId { get; init; } = string.Empty;

    public string BpmnXml { get; init; } = string.Empty;

    public IReadOnlyList<string> CompletedActivityIds { get; init; } = [];

    public IReadOnlyList<string> CurrentActivityIds { get; init; } = [];
}
