using System;

namespace AutoNate.Web.Persistence.Scaffolded;

/// <summary>
/// One published, immutable snapshot of a decision table (#110).
/// </summary>
/// <remarks>
/// Carries both the structured rules <em>and</em> the DMN that was generated from
/// them. The XML is kept rather than regenerated on demand for the reason the
/// version table exists at all: a later change to the generator would otherwise
/// silently change what a bound process decides, which is exactly what versioning
/// is here to prevent.
/// </remarks>
public partial class DecisionTableVersion
{
    public Guid Id { get; set; }

    public Guid DecisionTableId { get; set; }

    public int VersionNumber { get; set; }

    public string Name { get; set; } = null!;

    public string DecisionKey { get; set; } = null!;

    public string HitPolicy { get; set; } = null!;

    public string Inputs { get; set; } = null!;

    public string Outputs { get; set; } = null!;

    public string Rules { get; set; } = null!;

    /// <summary>The DMN deployed for this version, exactly as the engine received it.</summary>
    public string DmnXml { get; set; } = null!;

    public string DeploymentId { get; set; } = null!;

    /// <summary>
    /// The engine's id for this decision. What a deployed process binds to (#111),
    /// so republishing the table cannot change what an already-deployed definition
    /// decides.
    /// </summary>
    public string DecisionId { get; set; } = null!;

    public string DecisionDefinitionKey { get; set; } = null!;

    public int DecisionVersion { get; set; }

    public DateTime PublishedAtUtc { get; set; }
}
