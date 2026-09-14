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

    /// <summary>
    /// Exactly these elements are obliged to declare an effect (#429).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #418 removed the forgeable proof fields. The forgery moved rather than
    /// ending: it became <i>delete the obligation</i>. Setting one row's
    /// <c>declaredEffect</c> to null shrinks the E2E oracle by a cell, and every
    /// suite stays green — because nothing said which elements owed a
    /// declaration.
    /// </para>
    /// <para>
    /// PR #420 performed that exact move on <b>Service Task (Behavior)</b>, and
    /// deleted the count ratchet in the same commit. The demotion was correct and
    /// disclosed in prose; the problem is that nothing would have caught it
    /// otherwise, which is the definition of an unguarded claim.
    /// </para>
    /// <para>
    /// So the set is written down. Removing a declaration now requires deleting a
    /// name here too — an edit a reviewer sees in the diff, next to a test name
    /// that says what it means.
    /// </para>
    /// </remarks>
    [Fact]
    public void Exactly_these_elements_are_obliged_to_declare_an_effect()
    {
        string[] obliged =
        [
            "Ad-Hoc Sub-Process",
            "Call Activity",
            "Compensation End",
            "End Event (None)",
            "End Event (Terminate)",
            "Error End",
            "Escalation End",
            "Event-Based Gateway",
            "Exclusive Gateway (XOR)",
            "Inclusive Gateway (OR)",
            "Intermediate Catch (Conditional)",
            "Intermediate Catch (Message)",
            "Intermediate Catch (Signal)",
            "Intermediate Catch (Timer)",
            "Intermediate Throw (Compensation)",
            "Intermediate Throw (Escalation)",
            "Intermediate Throw (Message)",
            "Intermediate Throw (None)",
            "Intermediate Throw (Signal)",
            "Message End",
            "Parallel Gateway (AND)",
            "Receive Task",
            "Script Task",
            "Send Task",
            "Sequence Flow",
            "Signal End",
            "Start Event (None)",
            "Sub-Process (Embedded)",
            "User Task",
        ];

        var declaring = Elements()
            .Where(e => e!["declaredEffect"] is not null)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        var dropped = obliged.Except(declaring, StringComparer.Ordinal).ToList();
        var added = declaring.Except(obliged, StringComparer.Ordinal).ToList();

        Assert.True(
            dropped.Count == 0,
            "These elements are obliged to declare an effect and no longer do. Each one "
            + "silently removes a cell from the live-engine oracle:\n  "
            + string.Join("\n  ", dropped)
            + "\n\nIf the demotion is genuinely right, delete the name from `obliged` in this "
            + "test in the same commit, and say why in the row's `undeclaredReason` (#429).");

        Assert.True(
            added.Count == 0,
            "These elements declare an effect but are not in `obliged`, so the list has "
            + "drifted from the file and no longer pins anything:\n  "
            + string.Join("\n  ", added)
            + "\n\nAdd them here (#429).");
    }

    /// <summary>
    /// AC3's finding is visible to the merge gate, and may only shrink (#429).
    /// </summary>
    /// <remarks>
    /// The evidence file's own <c>$fields</c> says a null <c>declaredEffect</c> is
    /// <i>"a FINDING, not a pass"</i>. Nothing asserted that, so 38 elements sat
    /// in that state where only prose could see them. This is a ratchet, not a
    /// target: the number may fall as elements are proven, and a rise means an
    /// obligation was deleted.
    /// </remarks>
    [Fact]
    public void The_undeclared_elements_are_a_finding_and_the_count_only_falls()
    {
        // Measured, and LOWERED as elements gain declarations -- 38 when this
        // ratchet was written, 28 now that #325's AC5 tranche is proven. Never
        // raise it.
        const int Ceiling = 28;

        var undeclared = Elements()
            .Where(e => e!["declaredEffect"] is null)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count <= Ceiling,
            $"{undeclared.Count} elements have no declared effect, up from {Ceiling}. An "
            + "element losing its declaration removes a cell from the live-engine oracle "
            + "with every suite green, which is #429:\n  "
            + string.Join("\n  ", undeclared));
    }

    /// <summary>
    /// Only these keys may appear on an evidence row (#429).
    /// </summary>
    /// <remarks>
    /// <c>No_element_claims_a_proof_in_this_file</c> is a denylist of two names,
    /// so <c>"provenByRunningAnInstance": true</c> satisfies all three of its
    /// facts. A denylist cannot hold a rule of the form "this file records no
    /// proofs", because the space of names a proof can wear is unbounded. This is
    /// the same rule as an allowlist, where it is finite.
    /// </remarks>
    [Fact]
    public void An_evidence_row_carries_only_permitted_keys()
    {
        string[] permitted =
        [
            "name",
            "localName",
            "eventDefinition",
            "declaredEffect",
            "liveEngineTestsMentioningIt",
            "undeclaredReason",
        ];

        var offenders = Elements()
            .SelectMany(e => e!.AsObject()
                .Select(kv => kv.Key)
                .Where(k => !permitted.Contains(k, StringComparer.Ordinal))
                .Select(k => $"{e["name"]!.GetValue<string>()}: '{k}'"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These rows carry keys this file does not permit:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nProofs belong in a run, not a file (#408, #418). If the key is a legitimate "
            + "new field, add it to `permitted` here and document it in the file's `$fields` "
            + "— deliberately, in a diff, which is the whole point (#429).");
    }

    /// <summary>A reason for having no declaration belongs only where there is none.</summary>
    [Fact]
    public void An_undeclared_reason_appears_only_on_an_undeclared_row()
    {
        var incoherent = Elements()
            .Where(e => e!["undeclaredReason"] is not null && e["declaredEffect"] is not null)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            incoherent.Count == 0,
            "These rows declare an effect AND explain why they have not:\n  "
            + string.Join("\n  ", incoherent));
    }

    /// <summary>
    /// Every declared effect has a diagram in the live-engine oracle (#325 AC3, #429).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This assertion lived in <c>ExecutionEvidenceExecutionTests</c>, which is
    /// <c>RequiresService=Flowable</c> and therefore excluded from CI — so the
    /// one check that a declaration is actually exercised could not fail a merge.
    /// A declaration with no diagram is skipped by that class's own
    /// <c>MemberData</c> filter, silently.
    /// </para>
    /// <para>
    /// It reads the E2E source rather than referencing it: the two test projects
    /// do not reference each other, and a source-level guard is what the
    /// placement and property-coverage meta-guards in this suite already do.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_declared_effect_has_a_diagram_in_the_live_engine_oracle()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot.Path, "tests", "AutoNate.E2E.Tests", "ExecutionEvidenceExecutionTests.cs"));

        // The `case "<name>" =>` arms of that class's Diagram switch, and what
        // each one returns.
        //
        // The RIGHT-HAND SIDE matters (#433). The first version checked only that
        // an arm bearing the element's name existed, so `"Receive Task" => null,`
        // satisfied it while the live-engine oracle silently dropped from 19 cells
        // to 18 -- #429's forgery, relocated from the JSON to the switch. Both
        // verifiers found it independently.
        var arms = System.Text.RegularExpressions.Regex
            .Matches(source, """^\s{12}"(?<name>[^"]+)" =>(?<body>[^\n]*)""",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["body"].Value.Trim(),
                StringComparer.Ordinal);

        var empty = arms
            .Where(arm => arm.Value is "null," or "null")
            .Select(arm => arm.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            empty.Count == 0,
            "These diagram arms return `null`, which the oracle's own MemberData used to "
            + "skip silently:\n  "
            + string.Join("\n  ", empty)
            + "\n\nA declared element with no diagram must fail, not shrink the run (#433).");

        Assert.True(
            arms.Count > 0,
            "Found no diagram arms in ExecutionEvidenceExecutionTests.cs. This guard has "
            + "stopped reading the file it guards, which reads as a clean bill of health "
            + "against nothing — the exact failure mode #429 is about.");

        var missing = Elements()
            .Where(e => e!["declaredEffect"] is not null)
            .Select(e => e!["name"]!.GetValue<string>())
            .Where(name => !arms.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These elements declare an effect and have no minimal diagram in "
            + "ExecutionEvidenceExecutionTests, so that class skips them without saying so:\n  "
            + string.Join("\n  ", missing));
    }
}
