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

    // Flowable's `startUserId` — the user who started the process instance.
    // Drives the `startedby` selector tag.
    public string? StartUserId { get; init; }
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

    public DateTimeOffset? DueDate { get; init; }
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

    // Activities that were in flight when the process was cancelled. Always
    // empty for non-cancelled executions. Rendered with their own highlight
    // so the diagram doesn't pretend they finished normally.
    public IReadOnlyList<string> CancelledActivityIds { get; init; } = [];

    public IReadOnlyList<FlowableProcessVariable> Variables { get; init; } = [];
}

public sealed record class FlowableProcessVariable
{
    public string Name { get; init; } = string.Empty;

    public string? Type { get; init; }

    public string? Value { get; init; }
}

// Override-write payload for a single process variable. The diagram-detail GET
// flattens Flowable's typed values (json/number/bool) to a string via
// FormatVariableValue; the SPA parses the string back into a runtime value
// using the variable's Type as a hint before sending it here. When Type is
// null Flowable infers from the value's JSON kind. A null Value clears the
// variable on Flowable's side.
public sealed record class ProcessVariableUpdate
{
    public required string Name { get; init; }

    public object? Value { get; init; }

    public string? Type { get; init; }
}
