namespace AutoNate.Web.Services.Flowable;

/// <summary>
/// The DMN half of the engine: deploy a decision, evaluate it (#106).
/// </summary>
/// <remarks>
/// <para>
/// A separate interface from <see cref="IFlowableClient"/>, not an extension of
/// it. They address the same host and share its credentials, but they are two
/// engines with two REST services (<c>/service/</c> and <c>/dmn-api/</c>) and
/// two resource vocabularies; <see cref="IFlowableClient"/> is already past
/// thirty methods, and folding a second engine's surface into it would make
/// "which engine am I talking to" a question a reader has to answer per method.
/// </para>
/// <para>
/// **The DMN engine needs no additional component.** It ships enabled on the
/// image this repo already builds and pins — measured, not read: the container
/// carries <c>flowable-dmn-engine-8.0.0.jar</c> and answers on
/// <c>/flowable-rest/dmn-api/dmn-management/engine</c> with version 8.0.0. The
/// release stack, the published ports and the pinned digest are unchanged, which
/// is the finding #106 existed to establish before anything was built on top.
/// </para>
/// <para>
/// The path prefix is the one detail worth writing down: it is
/// <c>dmn-api/dmn-repository/…</c>, and <c>dmn-repository/…</c> without that
/// segment is a 404.
/// </para>
/// </remarks>
public interface IFlowableDecisionClient
{
    /// <summary>Deploys one DMN definition and returns what the engine made of it.</summary>
    Task<DecisionDeploymentInfo> DeployDecisionAsync(
        string decisionKey, string dmnXml, CancellationToken cancellationToken = default);

    /// <summary>The latest deployed version of a decision, or null when there is none.</summary>
    Task<DecisionDefinitionSummary?> GetLatestDecisionAsync(
        string decisionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deploys <paramref name="dmnXml"/> under <paramref name="decisionKey"/> only
    /// if no decision with that key exists yet (#111).
    /// </summary>
    /// <remarks>
    /// The pinned copy a process binds to is immutable by construction — the key
    /// carries the version — so deploying it twice would produce version 2 of
    /// something that must only ever have one. Every publish of every process
    /// referencing the same table version therefore lands on one deployment.
    /// </remarks>
    Task<DecisionDefinitionSummary> EnsureDecisionAsync(
        string decisionKey, string dmnXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a decision against <paramref name="inputs"/>.
    /// </summary>
    /// <remarks>
    /// A decision whose rules none of the inputs satisfy is **not** an error: it
    /// returns a result with no output rows. That is the DMN semantic — a hit
    /// policy may legitimately match nothing — and callers have to distinguish
    /// "no rule applied" from "the call failed", so it is a value rather than an
    /// exception. <see cref="DecisionEvaluationResult.Matched"/> is the question
    /// a caller actually has.
    /// </remarks>
    Task<DecisionEvaluationResult> EvaluateAsync(
        string decisionKey,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>What one DMN deployment produced.</summary>
public sealed record DecisionDeploymentInfo
{
    public required string DeploymentId { get; init; }

    public required string DecisionId { get; init; }

    public required string DecisionKey { get; init; }

    public required int DecisionVersion { get; init; }

    public required DateTimeOffset DeployedAtUtc { get; init; }
}

/// <summary>One deployed decision, as the repository service describes it.</summary>
public sealed record DecisionDefinitionSummary(
    string Id,
    string Key,
    string? Name,
    int Version,
    string? DeploymentId);

/// <summary>
/// One evaluation's outputs.
/// </summary>
/// <param name="Outputs">
/// One dictionary per matched rule. A single-hit policy yields at most one; a
/// collect policy yields as many as matched.
/// </param>
public sealed record DecisionEvaluationResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> Outputs)
{
    /// <summary>Whether any rule applied. Empty is a legitimate answer, not a failure.</summary>
    public bool Matched => Outputs.Count > 0;

    /// <summary>The first matched rule's outputs, or null when nothing matched.</summary>
    public IReadOnlyDictionary<string, object?>? First => Outputs.Count > 0 ? Outputs[0] : null;
}
