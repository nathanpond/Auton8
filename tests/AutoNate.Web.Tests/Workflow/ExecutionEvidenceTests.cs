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
    /// The oracle's whole effect vocabulary, in one place (#522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two marker effects arrived with #471: the four-name vocabulary could
    /// not say "more than one" or "one at a time", which is why the Loop Marker
    /// archetype -- the bug #325 opens on, and the reason the oracle exists --
    /// went unproven through the whole of M4b. The last two arrived with #522,
    /// for the same reason one step further out: nothing could say "an instance
    /// exists because the trigger fired" or "the host activity was cancelled",
    /// so no start event and no boundary event could declare anything.
    /// </para>
    /// <para>
    /// Three tests read this one array, and that is the point. It was a literal
    /// inside a single fact, with the file's <c>$fields</c> and a comment both
    /// asserting a pairing obligation that nothing checked.
    /// </para>
    /// </remarks>
    private static readonly string[] Observable =
    [
        "task-appears", "variable-written", "instance-waits", "instance-ends",
        "tasks-appear-together", "tasks-appear-in-turn",
        "instance-starts", "host-cancelled", "value-carried", "behavior-ran",
    ];

    /// <summary>
    /// Every declared effect is one the E2E oracle can observe (#325 AC3).
    /// </summary>
    /// <remarks>
    /// The file declares what would prove each element ran; the proof happens in
    /// <c>ExecutionEvidenceExecutionTests</c>, which starts an instance. This is
    /// the slim-tier half: it cannot run an engine, so it checks that the
    /// declarations are answerable — an effect name the oracle has no observer
    /// for is a declaration nothing can ever discharge.
    /// </remarks>
    [Fact]
    public void Every_declared_effect_is_one_the_oracle_can_observe()
    {
        // The two marker effects (#471). The four-name vocabulary could not say
        // "more than one" or "one at a time", which is why the Loop Marker
        // archetype -- the bug #325 opens on, and the reason the oracle exists --
        // went unproven through the whole of M4b. Every name here owes a negative
        // control in `InertDiagrams`; the `default` arm of `InertDiagram` throws
        // rather than letting one arrive without.
        string[] observable = Observable;

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
        // The PAIR, not just the name (#449). Pinning only the names left
        // "weaken the obligation" unguarded: changing Script Task from
        // `variable-written` to `instance-ends` kept every guard green and the
        // cell count intact while that cell stopped asserting the element's own
        // effect and started asserting something true of almost any linear
        // process. `instance-ends` is the observer #412 itself called "true of a
        // great many diagrams".
        (string Name, string Effect)[] obliged =
        [
            ("Ad-Hoc Sub-Process", "instance-waits"),
            ("Call Activity", "task-appears"),
            ("Compensation Boundary", "variable-written"),
            ("Compensation Marker", "variable-written"),
            ("Compensation End", "instance-ends"),
            ("Complex Gateway", "variable-written"),
            ("Conditional Boundary", "host-cancelled"),
            ("Conditional Start Event", "variable-written"),
            ("Data Object Reference", "value-carried"),
            ("End Event (None)", "instance-ends"),
            ("End Event (Terminate)", "instance-ends"),
            ("Error Boundary", "host-cancelled"),
            ("Error End", "instance-ends"),
            ("Error Start Event", "variable-written"),
            ("Escalation Boundary", "host-cancelled"),
            ("Escalation End", "instance-ends"),
            ("Escalation Start Event", "variable-written"),
            ("Event Sub-Process", "task-appears"),
            ("Event-Based Gateway", "instance-waits"),
            ("Exclusive Gateway (XOR)", "variable-written"),
            ("Inclusive Gateway (OR)", "variable-written"),
            ("Intermediate Catch (Conditional)", "instance-waits"),
            ("Intermediate Catch (Message)", "instance-waits"),
            ("Intermediate Catch (Signal)", "instance-waits"),
            ("Intermediate Catch (Timer)", "instance-waits"),
            ("Intermediate Throw (Compensation)", "instance-ends"),
            ("Intermediate Throw (Escalation)", "instance-ends"),
            ("Intermediate Throw (Message)", "instance-ends"),
            ("Intermediate Throw (None)", "instance-ends"),
            ("Intermediate Throw (Signal)", "instance-ends"),
            ("Message End", "instance-ends"),
            ("Multi-Instance (Parallel)", "tasks-appear-together"),
            ("Multi-Instance (Sequential)", "tasks-appear-in-turn"),
            ("Parallel Gateway (AND)", "variable-written"),
            ("Receive Task", "instance-waits"),
            ("Script Task", "variable-written"),
            ("Send Task", "instance-ends"),
            ("Service Task (Behavior)", "behavior-ran"),
            ("Sequence Flow", "instance-ends"),
            ("Signal End", "instance-ends"),
            ("Start Event (None)", "instance-ends"),
            ("Sub-Process (Embedded)", "task-appears"),
            ("Timer Boundary", "host-cancelled"),
            ("Timer Start Event", "instance-starts"),
            ("User Task", "task-appears"),
        ];

        var declaring = Elements()
            .Where(e => e!["declaredEffect"] is not null)
            .Select(e => (Name: e!["name"]!.GetValue<string>(),
                          Effect: e["declaredEffect"]!.GetValue<string>()))
            .Order()
            .ToList();

        var dropped = obliged.Except(declaring).Select(p => $"{p.Name} -> {p.Effect}").ToList();
        var added = declaring.Except(obliged).Select(p => $"{p.Name} -> {p.Effect}").ToList();

        Assert.True(
            dropped.Count == 0,
            "These (element, effect) pairs are obliged and no longer hold. A missing element "
            + "silently removes a cell from the live-engine oracle; a changed effect silently "
            + "empties one:\n  "
            + string.Join("\n  ", dropped)
            + "\n\nIf the demotion is genuinely right, delete the name from `obliged` in this "
            + "test in the same commit, and say why in the row's `undeclaredReason` (#429).");

        Assert.True(
            added.Count == 0,
            "These (element, effect) pairs are declared but not in `obliged`, so the list has "
            + "drifted from the file and no longer pins anything:\n  "
            + string.Join("\n  ", added)
            + "\n\nAdd them here (#429).");
    }

    /// <summary>
    /// AC3's finding is visible to the merge gate, and may only shrink (#429, #522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evidence file's own <c>$fields</c> says a null <c>declaredEffect</c> is
    /// <i>"a FINDING, not a pass"</i>. Nothing asserted that, so 38 elements sat
    /// in that state where only prose could see them. This is a ratchet, not a
    /// target: the number may fall as elements are proven, and a rise means an
    /// obligation was deleted.
    /// </para>
    /// <para>
    /// WHAT IT COUNTS CHANGED (#522). It counted rows with no
    /// <c>declaredEffect</c>, which meant a row carrying a written, measured
    /// <c>undeclaredReason</c> still counted as a finding -- and a measured
    /// reason is a legitimate terminal state for an element the product refuses
    /// to publish at all. Under the old rule the ratchet could not reach zero by
    /// construction, so it stopped being a thing anyone could finish. It now
    /// counts rows with <b>neither</b>, which is the set that is genuinely
    /// unaccounted for.
    /// </para>
    /// <para>
    /// That is only safe because prose cannot launder a row into "accounted":
    /// <c>Every_undeclared_reason_records_a_measurement</c> holds every reason to
    /// the same floor the manifest's reasons answer to. Without it this change
    /// would have converted a finding into a sentence.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_unaccounted_elements_are_a_finding_and_the_count_only_falls()
    {
        // Measured, and LOWERED as elements gain declarations -- 38 when this
        // ratchet was written, 22 once #325's AC5 tranche was proven, 16 under
        // #522's counting rule (19 rows declare nothing; 3 of them say why), 14
        // once #525 proved the two timer rows. Never raise it. It is a `<=`, so
        // leaving it high after a row is proven costs nothing today and hides the
        // next row that goes missing -- which is the whole failure this guards.
        const int Ceiling = 4;

        var unaccounted = Elements()
            .Where(e => e!["declaredEffect"] is null && e["undeclaredReason"] is null)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unaccounted.Count <= Ceiling,
            $"{unaccounted.Count} elements have neither a declared effect nor a reason for "
            + $"having none, up from {Ceiling}. An element losing its declaration removes a cell "
            + "from the live-engine oracle with every suite green, which is #429:\n  "
            + string.Join("\n  ", unaccounted));
    }

    /// <summary>
    /// A reason is a measurement or it is not a reason (#380, #522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ratchet above now treats a written <c>undeclaredReason</c> as a
    /// terminal state rather than a finding. That trade is only sound if a
    /// reason has to be worth something: otherwise the cheapest way to move the
    /// number is to type a sentence, and the ratchet measures prose.
    /// </para>
    /// <para>
    /// The floor is <c>BpmnSupportManifestTests.ReasonLooksLikeAMeasurement</c>
    /// -- the REAL predicate, CALLED, not a private copy of it. #380 is exactly
    /// what a copy costs: a meta-test that reimplements what it checks cannot
    /// notice the original drifting, and it did not, which is how that floor
    /// shipped admitting the one string it was written to reject. Three versions
    /// of its length, evidence and repetition rules are behind this one call.
    /// </para>
    /// <para>
    /// What this does NOT cover, stated rather than implied: a reason can be
    /// well-formed, specific and false. Nothing mechanical reads the engine to
    /// check it. That is the residue this guard leaves, and it is smaller than
    /// the residue of not having it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_undeclared_reason_records_a_measurement()
    {
        var gutted = Elements()
            .Select(e => (Name: e!["name"]!.GetValue<string>(),
                          Reason: (e!["undeclaredReason"]?.GetValue<string>() ?? "").Trim()))
            .Where(row => row.Reason.Length > 0)
            .Where(row => !BpmnSupportManifestTests.ReasonLooksLikeAMeasurement(row.Reason))
            .Select(row => $"{row.Name}: \"{row.Reason}\"")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            gutted.Count == 0,
            "These reasons no longer record a measurement, and the unaccounted ratchet counts "
            + "the row they sit on as accounted for:\n  "
            + string.Join("\n  ", gutted)
            + "\n\nA reason that says nothing is a row laundered out of the finding it belongs "
            + "in. Either measure something and write it down, or delete the reason and let the "
            + "ratchet see the row (#522).");
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
    /// <c>RequiresService=Flowable</c> and therefore full-local only — so the
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

    /// <summary>
    /// Exactly these elements may skip the oracle's entry check (#534).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #412's entry assertion is what stops a same-id stand-in satisfying a cell.
    /// <c>NeverEntered</c> exempts a row from it, which is correct for the two
    /// elements the engine records no activity instance for and catastrophic for
    /// anything else: adding <c>("userTask", null)</c> to that set would let a
    /// user-task row pass without the engine ever having entered it, and nothing
    /// would say so.
    /// </para>
    /// <para>
    /// That is the same shape as #429 — a set whose membership is load-bearing
    /// and unpinned. So it is pinned here, in the SLIM tier, where a merge can
    /// see it. Growing it is a visible diff next to a test name that says what it
    /// costs.
    /// </para>
    /// </remarks>
    [Fact]
    public void Exactly_these_elements_may_skip_the_entry_check()
    {
        string[] exempt =
        [
            """("boundaryEvent", "compensate")""",
            """("dataObjectReference", null)""",
        ];

        var source = OracleSource();
        var start = source.IndexOf("NeverEntered =", StringComparison.Ordinal);

        Assert.True(
            start >= 0,
            "Found no `NeverEntered` set in ExecutionEvidenceExecutionTests. Either the entry "
            + "exemption was removed — in which case delete this test in the same commit — or "
            + "this guard has stopped reading the thing it guards, which reads as a clean bill "
            + "of health against nothing (#429).");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, "The `NeverEntered` set is not in the shape this guard reads.");

        var declared = System.Text.RegularExpressions.Regex
            .Matches(source[start..end], """\("[a-zA-Z]+", (?:"[a-zA-Z]+"|null)\)""")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);

        var added = declared.Except(exempt, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            added.Count == 0,
            "These elements were added to `NeverEntered`, so their oracle cells no longer assert "
            + "that the engine entered the element at all:\n  "
            + string.Join("\n  ", added)
            + "\n\nThat exemption exists for elements the engine records NO activity instance "
            + "for — a compensation boundary and a data object reference, both measured. For "
            + "anything else it removes #412's stand-in check and a cell can pass on an element "
            + "that never ran. If the addition is genuinely right, add it here too and say what "
            + "was measured (#534).");

        var removed = exempt.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            removed.Count == 0,
            "These elements are no longer exempt from the entry check:\n  "
            + string.Join("\n  ", removed)
            + "\n\nIf the engine started recording them, that is good news and this list should "
            + "shrink in the same commit. If not, their cells now fail for the wrong reason.");
    }

    /// <summary>The live-engine oracle's source, read rather than referenced.</summary>
    /// <remarks>
    /// The two test projects do not reference each other, so the source-level
    /// read is how every cross-project guard in this class already works.
    /// </remarks>
    private static string OracleSource() => File.ReadAllText(Path.Combine(
        RepoRoot.Path, "tests", "AutoNate.E2E.Tests", "ExecutionEvidenceExecutionTests.cs"));

    /// <summary>
    /// The effect names of one control table in the live-engine oracle (#522).
    /// </summary>
    /// <remarks>
    /// Both tables are <c>TheoryData</c> initializers whose first column is the
    /// effect, so one reader serves both. The assertion that the reader still
    /// finds anything is in each caller, because a regex that silently stops
    /// matching reports a clean bill of health against nothing -- the failure
    /// mode this milestone has found more often than any other.
    /// </remarks>
    private static IReadOnlyCollection<string> ControlEffects(string table)
    {
        var source = OracleSource();
        var start = source.IndexOf($"TheoryData<string, string, string, string> {table}()",
            StringComparison.Ordinal);
        if (start < 0)
        {
            start = source.IndexOf($"TheoryData<string, string> {table}()", StringComparison.Ordinal);
        }

        if (start < 0) return [];

        var end = source.IndexOf("\n    };", start, StringComparison.Ordinal);
        if (end < 0) return [];

        return System.Text.RegularExpressions.Regex
            .Matches(source[start..end], """^\s+\{ "(?<effect>[^"]+)",""",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups["effect"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every observable effect owes a negative control, and that is now tested (#522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evidence file's <c>$fields</c> said <i>"every name here owes a
    /// negative control in ExecutionEvidenceExecutionTests.InertDiagrams"</i>,
    /// and the comment above <c>observable[]</c> in this file said it again.
    /// Nothing tested it. Adding a name to <c>observable[]</c> with no control
    /// was silent, and the <c>default</c> arm of <c>InertDiagram</c> that
    /// supposedly caught it only throws when a control is REQUESTED -- which is
    /// exactly what does not happen for a name no table mentions.
    /// </para>
    /// <para>
    /// #463's outcome rests entirely on those controls: an observer gutted to a
    /// tautology is caught by its negative control and by nothing else. A name
    /// admitted without one is an observer with no discrimination check at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_observable_effect_has_a_negative_control()
    {
        var controlled = ControlEffects("InertDiagrams");

        Assert.True(
            controlled.Count > 0,
            "Found no entries in ExecutionEvidenceExecutionTests.InertDiagrams. This guard has "
            + "stopped reading the table it guards, which reads as a clean bill of health "
            + "against nothing (#429, #522).");

        var uncontrolled = Observable
            .Where(effect => !controlled.Contains(effect))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            uncontrolled.Count == 0,
            "These effects are declared observable and have no negative control in "
            + "ExecutionEvidenceExecutionTests.InertDiagrams:\n  "
            + string.Join("\n  ", uncontrolled)
            + "\n\nAn observer with no inert diagram can be gutted to a tautology and every "
            + "cell that declares its effect passes on nothing (#463).");

        // And the other direction: a control for a name nothing may declare is a
        // cell running against a vocabulary that has moved on.
        var orphaned = controlled
            .Where(effect => !Observable.Contains(effect, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphaned.Count == 0,
            "These negative controls guard effects no row may declare:\n  "
            + string.Join("\n  ", orphaned));
    }

    /// <summary>
    /// A brand new effect proves it can hold, too (#522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An observer's positive arm is normally proven by the manifest rows that
    /// declare its effect -- sixteen cells ride on <c>instance-ends</c>. A name
    /// no row declares yet has none of that, and arrives carrying only a negative
    /// control. An observer hard-wired to <c>false</c> passes a negative control
    /// perfectly.
    /// </para>
    /// <para>
    /// So the obligation is conditional, and it retires itself: once a row
    /// declares the effect, that row's cell is the positive proof and the entry
    /// in <c>LiveControls</c> may go. Nothing has to remember to remove it, and
    /// nothing has to remember to add one for the next new name either.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_observable_effect_no_row_declares_has_a_positive_control()
    {
        var declared = Elements()
            .Select(e => e!["declaredEffect"]?.GetValue<string>())
            .Where(effect => effect is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        var undeclaredNames = Observable.Where(effect => !declared.Contains(effect)).ToList();

        // Not vacuous by accident. When every name is declared there is nothing
        // to require, and that is the healthy end state -- but it must be reached
        // because rows arrived, not because the reader broke, so the table read
        // is asserted whenever the requirement is live.
        if (undeclaredNames.Count == 0) return;

        var proven = ControlEffects("LiveControls");

        Assert.True(
            proven.Count > 0,
            "Found no entries in ExecutionEvidenceExecutionTests.LiveControls, while these "
            + "effects have no declaring row to prove them:\n  "
            + string.Join("\n  ", undeclaredNames.Order(StringComparer.Ordinal)));

        var unproven = undeclaredNames
            .Where(effect => !proven.Contains(effect))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unproven.Count == 0,
            "These effects are declared observable, no evidence row declares one, and there is "
            + "no positive control for them in ExecutionEvidenceExecutionTests.LiveControls:\n  "
            + string.Join("\n  ", unproven)
            + "\n\nNothing asserts their observer can ever report HELD, so an observer returning "
            + "a constant false would satisfy every guard in this suite (#522).");
    }
}
