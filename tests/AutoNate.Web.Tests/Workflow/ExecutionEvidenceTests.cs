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
    /// A proof claim carries what was observed (#325).
    /// </summary>
    /// <remarks>
    /// `provenByStartingAnInstance: true` with no `measured` text is the shape this
    /// milestone keeps finding: an assertion whose evidence is its own assertion.
    /// </remarks>
    [Fact]
    public void Every_proof_claim_says_what_the_engine_showed()
    {
        var unsupported = Elements()
            .Where(e => e!["provenByStartingAnInstance"]?.GetValue<bool>() == true)
            .Where(e => string.IsNullOrWhiteSpace(e!["measured"]?.GetValue<string>()))
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unsupported.Count == 0,
            "These elements claim they were proven by starting an instance and record nothing "
            + "the engine showed:\n  " + string.Join("\n  ", unsupported)
            + "\n\nRe-run tools/bpmn-execution-probe/probe.py rather than writing the field by "
            + "hand -- a proof whose evidence is its own assertion is what #325 exists to end.");
    }

    /// <summary>
    /// An element cannot be proven without declaring what would prove it (#325 AC3).
    /// </summary>
    [Fact]
    public void Nothing_is_proven_without_a_declared_effect()
    {
        var impossible = Elements()
            .Where(e => e!["provenByStartingAnInstance"]?.GetValue<bool>() == true)
            .Where(e => e!["declaredEffect"] is null)
            .Select(e => e!["name"]!.GetValue<string>())
            .ToList();

        Assert.True(
            impossible.Count == 0,
            "These claim a proof with no declared effect, so nothing says what was observed "
            + "or why it counts:\n  " + string.Join("\n  ", impossible));
    }

    /// <summary>
    /// The oracle proves a real number of elements, and that number is recorded (#325 AC4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A literal, deliberately. AC4 says a green first run means the oracle is not
    /// measuring anything, so the count is a fact about a measurement rather than
    /// something derived from the file it is checking. Raising it is an edit a
    /// reviewer sees, and lowering it is the alarm.
    /// </para>
    /// <para>
    /// 19 of the 20 elements with a declared effect are proven by starting an
    /// instance. The twentieth, Service Task (Behavior), runs its behaviour as an
    /// HTTP callback into the Auton8 app, so the engine alone cannot prove it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Nineteen_elements_are_proven_by_starting_an_instance()
    {
        var proven = Elements().Count(e => e!["provenByStartingAnInstance"]?.GetValue<bool>() == true);
        var declared = Elements().Count(e => e!["declaredEffect"] is not null);

        Assert.Equal(20, declared);
        Assert.Equal(19, proven);

        // And the gap between "executes" and "proven" is the honest headline: 57
        // rows claim it, 19 have been run. That is not a defect -- it is the
        // measurement AC4 asked for, and pinning it stops it being forgotten.
        Assert.Equal(57, Elements().Count);
    }

    private static string ProbeResultsPath => Path.Combine(
        RepoRoot.Path, "tools", "bpmn-execution-probe", "execution-results.json");

    private static IReadOnlyDictionary<string, (string Verdict, string Detail)> ProbeResults()
    {
        var rows = JsonNode.Parse(File.ReadAllText(ProbeResultsPath))!.AsArray();

        return rows.ToDictionary(
            r => r!["name"]!.GetValue<string>(),
            r => (r!["verdict"]!.GetValue<string>(), r["detail"]?.GetValue<string>() ?? ""),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The record says what the ENGINE said, not what someone typed (#408).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first version of this class checked the evidence file against itself —
    /// that rows lined up with the manifest, that a proof carried <em>some</em>
    /// text, that three counts equalled three literals. Those counts were the only
    /// thing holding the record to reality, and they are preserved by any edit
    /// that swaps one row's status for another's.
    /// </para>
    /// <para>
    /// Verification demonstrated the cost: giving <b>Manual Task</b> a proof and
    /// demoting Receive Task kept the counts at 20/19/57 and the suite green.
    /// Manual Task is one of the three founding members of the class #325 exists
    /// to catch — it deploys and passes straight through creating nothing — and
    /// the suite would have certified that it creates a task.
    /// </para>
    /// <para>
    /// So the record is now compared against <c>execution-results.json</c>, which
    /// the probe writes from what the engine actually returned. A fabricated proof
    /// fails because the probe never heard of that element; a dropped one fails
    /// because the probe did.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_proof_matches_what_the_probe_recorded()
    {
        var probe = ProbeResults();

        Assert.True(
            probe.Count > 15,
            $"The probe results hold {probe.Count} rows. If that file was emptied or moved, "
            + "this guard is comparing against nothing (#408).");

        var wrong = new List<string>();

        foreach (var element in Elements())
        {
            var name = element!["name"]!.GetValue<string>();
            var claimsProof = element["provenByStartingAnInstance"]?.GetValue<bool>() == true;

            if (!probe.TryGetValue(name, out var recorded))
            {
                if (claimsProof)
                {
                    wrong.Add($"{name}: claims a proof, but the probe never attempted it");
                }

                continue;
            }

            var proved = string.Equals(recorded.Verdict, "proved", StringComparison.Ordinal);

            if (claimsProof != proved)
            {
                wrong.Add(
                    $"{name}: record says proven={claimsProof}, the probe says verdict="
                    + $"'{recorded.Verdict}'");
                continue;
            }

            if (!proved) continue;

            var measured = element["measured"]?.GetValue<string>() ?? "";
            if (!string.Equals(measured, recorded.Detail, StringComparison.Ordinal))
            {
                wrong.Add(
                    $"{name}: `measured` is not what the probe observed.\n"
                    + $"      record: {measured}\n"
                    + $"      probe : {recorded.Detail}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "The execution record disagrees with what the probe actually observed:\n  "
            + string.Join("\n  ", wrong)
            + "\n\nRe-run tools/bpmn-execution-probe/probe.py and commit both files together. "
            + "Editing the record by hand is how Manual Task -- an element that deploys and "
            + "creates nothing, which is why it is withdrawn -- got certified as creating a "
            + "task with the suite green (#408).");
    }

    /// <summary>A proved probe row is not quietly dropped from the record (#408).</summary>
    [Fact]
    public void Every_probe_proof_appears_in_the_record()
    {
        var recorded = Elements()
            .Where(e => e!["provenByStartingAnInstance"]?.GetValue<bool>() == true)
            .Select(e => e!["name"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        var missing = ProbeResults()
            .Where(r => r.Value.Verdict == "proved")
            .Select(r => r.Key)
            .Where(name => !recorded.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "The probe proved these elements and the record does not say so:\n  "
            + string.Join("\n  ", missing)
            + "\n\nThe other direction of #408 -- a proof can be dropped as easily as invented, "
            + "and the counts absorb both.");
    }

}
