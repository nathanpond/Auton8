using System;
using System.Collections.Generic;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowModel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string ProcessKey { get; set; } = null!;

    public string BpmnXml { get; set; } = null!;

    public bool IsDraft { get; set; }

    public int DraftVersionNumber { get; set; }

    public int? PublishedVersionNumber { get; set; }

    public string? ActiveProcessInstanceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? LastDeploymentId { get; set; }

    public string? LastProcessDefinitionId { get; set; }

    public string? LastProcessDefinitionKey { get; set; }

    public int? LastProcessDefinitionVersion { get; set; }

    public DateTime? LastDeployedAtUtc { get; set; }
}
