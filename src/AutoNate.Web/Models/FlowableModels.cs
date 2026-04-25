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

    public string? TaskDefinitionKey { get; init; }

    public string? Assignee { get; init; }

    public string? ProcessInstanceId { get; init; }

    public string? ProcessDefinitionId { get; init; }

    public string? ProcessDefinitionName { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record class WorkflowExecutionSummary
{
    public string Id { get; set; } = string.Empty;

    public string? WorkflowModelName { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? LastActivityAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? CurrentStep { get; set; }
}

public sealed record class WorkflowExecutionDiagramDetail
{
    public string ExecutionId { get; init; } = string.Empty;

    public string BpmnXml { get; init; } = string.Empty;

    public IReadOnlyList<string> CompletedActivityIds { get; init; } = [];

    public IReadOnlyList<string> CurrentActivityIds { get; init; } = [];

    public IReadOnlyList<FlowableProcessVariable> Variables { get; init; } = [];
}

public sealed record class FlowableProcessVariable
{
    public string Name { get; init; } = string.Empty;

    public string? Type { get; init; }

    public string? Value { get; init; }
}
