namespace AutoNate.Web.Models;

public sealed record class WorkflowModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ProcessKey { get; init; } = string.Empty;

    public string BpmnXml { get; init; } = string.Empty;

    public bool IsDraft { get; init; } = true;

    public int DraftVersionNumber { get; init; } = 1;

    public int? PublishedVersionNumber { get; init; }

    public WorkflowDeploymentInfo? LastDeployment { get; init; }

    public string? ActiveProcessInstanceId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record class WorkflowModelVersion
{
    public Guid Id { get; init; }

    public Guid WorkflowModelId { get; init; }

    public int VersionNumber { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ProcessKey { get; init; } = string.Empty;

    public string BpmnXml { get; init; } = string.Empty;

    public WorkflowDeploymentInfo Deployment { get; init; } = new();

    public DateTimeOffset PublishedAtUtc { get; init; }
}

public sealed record class WorkflowDeploymentInfo
{
    public string DeploymentId { get; init; } = string.Empty;

    public string ProcessDefinitionId { get; init; } = string.Empty;

    public string ProcessDefinitionKey { get; init; } = string.Empty;

    public int ProcessDefinitionVersion { get; init; }

    public DateTimeOffset DeployedAtUtc { get; init; }
}
