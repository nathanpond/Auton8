using System;

namespace AutoNate.Web.Persistence.Scaffolded;

/// <summary>
/// An author's editable decision table (#110).
/// </summary>
/// <remarks>
/// <para>
/// The <c>workflow_models</c> shape, because the problem is the same one: changing
/// a table must not silently change what already-running processes decide. A draft
/// is edited freely; publishing creates an immutable
/// <see cref="DecisionTableVersion"/>; a process binds to a version.
/// </para>
/// <para>
/// <b>The rules are structured data, not DMN XML.</b> The DMN is generated at
/// publish. Storing hand-edited XML would make "a rule whose cells do not satisfy
/// their declared types is rejected at save" impossible to enforce — you cannot
/// validate cells you did not model.
/// </para>
/// </remarks>
public partial class DecisionTable
{
    public Guid Id { get; set; }

    /// <summary>The key a business rule task references. Unique, like a process key.</summary>
    public string DecisionKey { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// FIRST, UNIQUE, ANY, COLLECT, RULE ORDER, OUTPUT ORDER, PRIORITY.
    /// </summary>
    /// <remarks>
    /// Stored as the engine's own spelling rather than an enum, so a policy Flowable
    /// adds does not need a schema change to become expressible. The set an author
    /// may choose from is validated at save.
    /// </remarks>
    public string HitPolicy { get; set; } = "FIRST";

    /// <summary>JSON array of <c>{ id, label, name, typeRef }</c>.</summary>
    public string Inputs { get; set; } = "[]";

    /// <summary>JSON array of <c>{ id, label, name, typeRef }</c>.</summary>
    public string Outputs { get; set; } = "[]";

    /// <summary>JSON array of <c>{ id, inputEntries[], outputEntries[] }</c>.</summary>
    public string Rules { get; set; } = "[]";

    public bool IsDraft { get; set; } = true;

    public int DraftVersionNumber { get; set; } = 1;

    public int? PublishedVersionNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? LastDeploymentId { get; set; }

    public string? LastDecisionId { get; set; }

    public string? LastDecisionKey { get; set; }

    public int? LastDecisionVersion { get; set; }

    public DateTime? LastDeployedAtUtc { get; set; }
}
