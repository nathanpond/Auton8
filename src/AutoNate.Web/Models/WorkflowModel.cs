using System.Text.Json;

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

    // Mirrors Flowable's suspension flag on the latest published process
    // definition. Null when the workflow has not been deployed yet. Populated
    // by the API endpoints from a Flowable lookup; not stored locally so it
    // can never drift from Flowable's own state.
    public bool? IsSuspended { get; init; }

    public string? ActiveProcessInstanceId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    // Per-model defaults that get merged in at process-instance start (under
    // any explicit values the caller passes). Null when the user hasn't
    // configured any. Round-tripped through the workflow_models.default_variables
    // jsonb column.
    public IReadOnlyList<WorkflowDefaultVariable>? DefaultVariables { get; init; }
}

public sealed record class WorkflowDefaultVariable
{
    public string Name { get; init; } = string.Empty;

    // One of: "string", "number", "boolean", "json". The SPA picks the
    // type when authoring; the start-instance handler uses it to coerce
    // the JsonElement Value into the right CLR shape for Flowable.
    public string Type { get; init; } = "string";

    public JsonElement? Value { get; init; }
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

/// <summary>
/// One deployed definition of a set (#169). A single-pool workflow has exactly
/// one, equal to the primary; a collaboration has one per executable pool.
/// </summary>
public sealed record class WorkflowDeployedDefinition
{
    public required string ProcessDefinitionKey { get; init; }

    public required string ProcessDefinitionId { get; init; }

    public int ProcessDefinitionVersion { get; init; }

    /// <summary>The engine's name for it -- the participant's name, for a pool.</summary>
    public string? Name { get; init; }
}

/// <summary>
/// What a participant of a collaboration would deploy as (#169).
/// </summary>
public sealed record WorkflowParticipantInfo(
    string Id,
    string Name,
    string? ProcessId,
    bool IsExecutable,
    bool IsPrimary);

public sealed record class WorkflowDeploymentInfo
{
    /// <summary>
    /// The shared version identity of the set (#169). Flowable already groups the
    /// definitions of one uploaded file under one deployment, co-versioned and
    /// all-or-nothing, so this id IS the identity the AC asks for.
    /// </summary>
    public string DeploymentId { get; init; } = string.Empty;

    /// <summary>
    /// Every definition the deployment produced (#169). Empty on rows written
    /// before this existed; readers fall back to the primary columns below.
    /// </summary>
    public IReadOnlyList<WorkflowDeployedDefinition> Definitions { get; init; } = [];

    public string ProcessDefinitionId { get; init; } = string.Empty;

    public string ProcessDefinitionKey { get; init; } = string.Empty;

    public int ProcessDefinitionVersion { get; init; }

    public DateTimeOffset DeployedAtUtc { get; init; }
}
