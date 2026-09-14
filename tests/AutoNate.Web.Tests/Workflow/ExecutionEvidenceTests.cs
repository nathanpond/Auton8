using System.Text.Json.Nodes;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// "It executes" must mean an instance ran it, not that it deployed (#325).
/// </summary>
/// <remarks>
/// <para>
/// Epic #40's founding complaint is <i>"a diagram that draws fine and then does
/// nothing."</i> Two failure modes satisfy it, and only one was ever instrumented:
/// publish accepts and the engine refuses (covered), versus publish accepts, the
/// engine deploys, and <b>the element never runs</b> (covered by nothing).
/// </para>
/// <para>
/// The second is not hypothetical. Flowable accepted <c>standardLoopCharacteristics</c>
/// at deployment and then ran the activity exactly once; Manual Task and Task
/// (Generic) deploy and pass straight through creating nothing. All three were
/// found by a person running a control by hand, because every instrument stopped
/// at "it deployed".
/// </para>
/// <para>
/// <c>tools/bpmn-execution-probe/probe.py</c> starts an instance per element and
/// observes the declared effect against a live engine. This guards the RECORD it
/// produces: that the evidence file keeps saying what was measured, that an
/// element cannot quietly acquire a proof it never had, and that an undeclared
/// effect stays a finding rather than becoming a pass.
/// </para>
/// </remarks>
public sealed class ExecutionEvidenceTests
{
    private static string EvidencePath =>
        Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-execution-evidence.json");

    private static string ManifestPath =>
        Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-support.json");

    private static JsonArray Elements() =>
        JsonNode.Parse(File.ReadAllText(EvidencePath))!["elements"]!.AsArray();

    [Fact]
    public void The_evidence_file_covers_every_element_the_manifest_says_executes()
    {
        var executes = JsonNode.Parse(File.ReadAllText(ManifestPath))!["elements"]!.AsArray()
            .Where(e => e!["engine"]!.GetValue<string>() == "executes")
            .Select(e => e!["name"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        var covered = Elements()
            .Select(e => e!["name"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        var missing = executes.Except(covered, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "These elements claim `engine: executes` and have no row in the execution "
            + "evidence file, so nothing records whether they were ever RUN:\n  "
            + string.Join("\n  ", missing)
            + "\n\nAdd a row. An element promoted to `executes` without one is exactly how "
            + "Loop Marker shipped (#325).");

        // And the other direction: a row for an element that no longer executes is
        // a claim about nothing.
        var stale = covered.Except(executes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "Evidence rows for elements that no longer execute:\n  " + string.Join("\n  ", stale));
    }









    /// <summary>
    /// Every declared effect is one the E2E oracle can observe (#325 AC3).
    /// </summary>
    /// <remarks>
    /// The file declares what would prove each element ran; the proof happens in
    /// <c>ExecutionEvidenceExecutionTests</c>, which starts an instance. This is
    /// the in-CI half: it cannot run an engine, so it checks that the
    /// declarations are answerable — an effect name the oracle has no observer
    /// for is a declaration nothing can ever discharge.
    /// </remarks>
    [Fact]
    public void Every_declared_effect_is_one_the_oracle_can_observe()
    {
        string[] observable = ["task-appears", "variable-written", "instance-waits", "instance-ends"];

        var unobservable = Elements()
            .Select(e => (Name: e!["name"]!.GetValue<string>(),
                          Effect: e["declaredEffect"]?.GetValue<string>()))
            .Where(row => row.Effect is not null)
            .Where(row => !observable.Contains(row.Effect, StringComparer.Ordinal))
            .Select(row => $"{row.Name}: '{row.Effect}'")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unobservable.Count == 0,
            "These elements declare an effect no observer in ExecutionEvidenceExecutionTests "
            + "knows how to see, so the declaration can never be discharged:\n  "
            + string.Join("\n  ", unobservable));
    }

    /// <summary>
    /// This file claims no proofs of its own (#408, #418).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to carry <c>provenByStartingAnInstance</c> and <c>measured</c>, and
    /// that was the defect: a proof recorded in a file is a claim, and
    /// verification falsified it twice — certifying that <b>Manual Task creates a
    /// runtime task</b>, first with a one-file edit and then, after the fix, with
    /// a consistent two-file edit.
    /// </para>
    /// <para>
    /// The proof moved into a run. This pins that it does not move back: a field
    /// asserting a proof here is a transcript again, and a transcript can be
    /// typed.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_element_claims_a_proof_in_this_file()
    {
        var claiming = Elements()
            .Where(e => e!["provenByStartingAnInstance"] is not null || e["measured"] is not null)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            claiming.Count == 0,
            "These rows record a proof in the evidence file:\n  "
            + string.Join("\n  ", claiming)
            + "\n\nProofs belong in ExecutionEvidenceExecutionTests, which starts an instance "
            + "and observes the effect. A proof written here is a claim a person typed, and "
            + "#408 and #418 are what that costs (#418).");
    }

}
