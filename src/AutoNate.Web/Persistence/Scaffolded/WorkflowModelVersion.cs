using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class WorkflowModelVersion
{
    public Guid Id { get; set; }

    public Guid WorkflowModelId { get; set; }

    public int VersionNumber { get; set; }

    public string Name { get; set; } = null!;

    public string ProcessKey { get; set; } = null!;

    public string BpmnXml { get; set; } = null!;

    public string DeploymentId { get; set; } = null!;

    public string ProcessDefinitionId { get; set; } = null!;

    public string ProcessDefinitionKey { get; set; } = null!;

    public int ProcessDefinitionVersion { get; set; }

    public DateTime PublishedAtUtc { get; set; }
}
