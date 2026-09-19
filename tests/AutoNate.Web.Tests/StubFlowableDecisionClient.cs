using System.Net;
using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Tests;

/// <summary>
/// A recording stand-in for the DMN engine (#110).
/// </summary>
/// <remarks>
/// <para>
/// The real engine's behaviour is pinned by <c>DecisionEngineTests</c> and
/// <c>GeneratedDecisionTableTests</c>, which are <c>RequiresService=Flowable</c>.
/// This exists so the publish and try-it PATHS can be tested in slim, where there
/// is no engine — what it must not do is invent behaviour the engine does not have.
/// </para>
/// <para>
/// So it records what it was asked and replays what it was told, and every
/// behavioural claim it makes is one a live test also makes: a deployment returns a
/// version that increments, a failure is a <see cref="FlowableRequestException"/>
/// carrying a status, and an evaluation returns rows.
/// </para>
/// </remarks>
public sealed class StubFlowableDecisionClient : IFlowableDecisionClient
{
    private int _version;

    /// <summary>Every call, in order: <c>Deploy:key</c>, <c>Evaluate:key</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The DMN of each deployment, so a test can assert what was sent.</summary>
    public List<string> DeployedXml { get; } = [];

    /// <summary>When set, <see cref="DeployDecisionAsync"/> throws it instead of deploying.</summary>
    public FlowableRequestException? DeployThrows { get; set; }

    /// <summary>What <see cref="EvaluateAsync"/> returns. Empty means no rule matched.</summary>
    public List<IReadOnlyDictionary<string, object?>> EvaluationOutputs { get; } = [];

    public Task<DecisionDeploymentInfo> DeployDecisionAsync(
        string decisionKey, string dmnXml, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Deploy:{decisionKey}");

        if (DeployThrows is not null) throw DeployThrows;

        DeployedXml.Add(dmnXml);
        _version++;

        return Task.FromResult(new DecisionDeploymentInfo
        {
            DeploymentId = $"dep-{_version}",
            DecisionId = $"{decisionKey}:{_version}:stub",
            DecisionKey = decisionKey,
            DecisionVersion = _version,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public Task<DecisionDefinitionSummary?> GetLatestDecisionAsync(
        string decisionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<DecisionDefinitionSummary?>(_version == 0
            ? null
            : new DecisionDefinitionSummary(
                $"{decisionKey}:{_version}:stub", decisionKey, decisionKey, _version, $"dep-{_version}"));

    public Task<DecisionEvaluationResult> EvaluateAsync(
        string decisionKey,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"Evaluate:{decisionKey}");
        return Task.FromResult(new DecisionEvaluationResult(EvaluationOutputs.ToList()));
    }

    /// <summary>A refusal shaped like the engine's own, for the failed-deploy path.</summary>
    public static FlowableRequestException Refusal(string message) =>
        new(HttpStatusCode.BadRequest, "deploy the decision", message);
}
