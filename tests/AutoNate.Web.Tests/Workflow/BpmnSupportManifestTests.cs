using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// The BPMN support manifest is the one source of truth (#107): what the studio
/// offers, and what the engine will run.
/// </summary>
/// <remarks>
/// <para>
/// Before it there were three lists — the studio's types modal, the palette, and
/// the <c>UnsupportedRuntime*</c> sets in <see cref="WorkflowBpmnXml"/> — kept in
/// step by hand, with nothing failing when one was missed. #103 deployed all 68
/// entries to a running Flowable 8.0.0 and measured the result: 22 elements were
/// advertised as "coming soon" that deployed unhindered, and 25 were refused at
/// runtime that the engine runs.
/// </para>
/// <para>
/// So these tests are not about the manifest's contents. They are about the
/// property that made the old arrangement fail: that a consumer could disagree
/// with the source and nothing would notice.
/// </para>
/// </remarks>
public sealed class BpmnSupportManifestTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private static string SharedManifestPath =>
        Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-support.json");

    private static string StudioPath => Path.Combine(
        RepoRoot.Path, "src", "AutoNate.Spa", "src", "pages", "workflow", "WorkflowStudio.tsx");

    private static string SpaSupportModulePath => Path.Combine(
        RepoRoot.Path, "src", "AutoNate.Spa", "src", "lib", "bpmn", "support.ts");

    private static string InventoryRowsPath => Path.Combine(
        RepoRoot.Path, "tests", "fixtures", "bpmn-inventory", "rows.json");

    // ── The totals, which nothing guarded ───────────────────────────────────

    /// <summary>
    /// The manifest's tallies are pinned (#330).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing asserted any total. Flipping one row's <c>studio</c> value — the
    /// single most likely edit anyone makes to this file — left the whole suite
    /// green at 214/214, and the coverage claim in M4's description said
    /// otherwise:
    /// </para>
    /// <para>
    /// <i>"Guarded going forward by BpmnSupportManifestTests, which counts the
    /// manifest rather than trusting prose."</i>
    /// </para>
    /// <para>
    /// That sentence was written in the same note that corrected an earlier
    /// arithmetic drift, and it was false when written: the guard it promises did
    /// not exist. Two commits later the sum drifted again — Send Task was
    /// withdrawn (#316) and the totals were not carried, so the description read
    /// 55/10/4 while the file held 54/11/4.
    /// </para>
    /// <para>
    /// Pinning the numbers is deliberately annoying. Changing the manifest is
    /// supposed to be a decision with a paper trail, not a silent edit, and the
    /// failure message says where the paper trail lives. This is the whole of the
    /// guard's value — it does NOT check that the counts are *right*, only that
    /// changing them is noticed.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_manifest_tallies_are_what_the_coverage_claim_says_they_are()
    {
        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();

        int CountBy(string field, string value) =>
            elements.Count(e => e![field]!.GetValue<string>() == value);

        var actual = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["total"] = elements.Count,
            ["studio:supported"] = CountBy("studio", "supported"),
            ["studio:withdrawn"] = CountBy("studio", "withdrawn"),
            ["studio:coming-soon"] = CountBy("studio", "coming-soon"),
            ["engine:executes"] = CountBy("engine", "executes"),
            ["engine:cannot-execute"] = CountBy("engine", "cannot-execute"),
            ["engine:annotation"] = CountBy("engine", "annotation"),
        };

        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["total"] = 69,
            // 54/11 until #340 withdrew the five message rows: the studio cannot
            // create or name a <bpmn:message> root, so they were never authorable.
            // The engine axis is unchanged -- all five still `engine: executes`.
            // 49/4 until #265 moved Data Input and Data Output to coming-soon: the
            // manifest called them SUPPORTED -- "authorable in the studio today" --
            // while no editor for an ioSpecification exists, and the palette's own
            // exclusion line excused the gap by naming an editor that was never
            // built. The engine axis is unchanged: both still `engine: executes`.
            // 47/16 until #328 made Send Task authorable again: the studio's message
            // editor now writes the behaviour key the expansion needs, so a send task
            // it produces is deployable. #316 had withdrawn it because nothing in the
            // studio could write that key. The engine axis never moved -- the engine
            // always ran a correctly configured send task.
            // 48/6 and 51/9 until #111 made Business Rule Task authorable and
            // executable BY EXPANSION. Both axes moved on one row: the raw element
            // is refused at DEPLOY (NoClassDefFoundError org/kie/api -- KIE is
            // absent from the image, the DMN engine is NOT, which the old reason
            // conflated), so publish rewrites the deployed copy into a
            // flowable:type=dmn service task. Same shape as Complex Gateway (#218).
            ["studio:supported"] = 49,
            ["studio:withdrawn"] = 15,
            ["studio:coming-soon"] = 5,
            ["engine:executes"] = 52,
            ["engine:cannot-execute"] = 8,
            ["engine:annotation"] = 9,
        };

        var drifted = expected
            .Where(pair => actual[pair.Key] != pair.Value)
            .Select(pair => $"{pair.Key}: manifest has {actual[pair.Key]}, this guard expects {pair.Value}")
            .ToList();

        Assert.True(
            drifted.Count == 0,
            "The BPMN support manifest's tallies changed.\n  "
            + string.Join("\n  ", drifted)
            + "\n\nIf that was deliberate, update BOTH this guard and the coverage map in the "
            + "milestone description that quotes these numbers — they have drifted apart twice "
            + "already (#330), each time because the manifest moved and the prose did not.");

        // The three studio statuses partition the file: a new status that no
        // consumer understands would otherwise slip in under a stable total.
        Assert.Equal(
            actual["total"],
            actual["studio:supported"] + actual["studio:withdrawn"] + actual["studio:coming-soon"]);
        Assert.Equal(
            actual["total"],
            actual["engine:executes"] + actual["engine:cannot-execute"] + actual["engine:annotation"]);
    }

    /// <summary>
    /// Which elements are withdrawn, not merely how many (#342).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #330's guard is a total-only ratchet, and a tally-preserving swap walks
    /// straight past it:
    /// </para>
    /// <para>
    /// <code>
    ///   Send Task     withdrawn -> supported
    ///   Receive Task  supported -> withdrawn
    ///     -> 54/11/4 unchanged, suite GREEN at 49/49
    /// </code>
    /// </para>
    /// <para>
    /// So the one element #316 withdrew for drawing-fine-and-doing-nothing could
    /// be silently reinstated as <c>supported</c> — the precise claim the manifest
    /// exists to make honest. Counting was never the point; <b>which</b> elements
    /// is.
    /// </para>
    /// <para>
    /// Only the two non-default statuses are pinned. Listing all 54 supported rows
    /// would make every addition a two-file edit for no signal, while a row
    /// LEAVING supported necessarily enters one of these lists.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_withdrawn_and_coming_soon_elements_are_exactly_these()
    {
        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();

        IReadOnlyList<string> NamesWith(string studio) => elements
            .Where(e => e!["studio"]!.GetValue<string>() == studio)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        string[] expectedWithdrawn =
        [
            "Boundary Event (None)", "Cancel Boundary", "Cancel End", "Compensation Start Event",
            "Intermediate Catch (Link)", "Intermediate Catch (Message)", "Intermediate Throw (Link)",
            "Intermediate Throw (Message)", "Loop Marker", "Manual Task", "Message Boundary",
            "Message End", "Message Start Event", "Task (Generic)", "Transaction",
        ];
        // Business Rule Task left this list in #111. The guard's own words are the
        // right bar to answer: "an element moving INTO supported is a claim that an
        // author can configure it and it runs." Both halves hold, and both were
        // measured rather than assumed.
        //
        // An author configures it: the studio writes autonate:decisionKey, and a
        // task with none is refused at prepare naming the element.
        //
        // And it runs -- BY EXPANSION, which is the part worth stating. The raw
        // element cannot deploy at all (NoClassDefFoundError org/kie/api; KIE is
        // absent from the image, the DMN engine is NOT), so publish rewrites the
        // deployed copy into a flowable:type=dmn service task. Probed end to end:
        // the expanded form wrote route='big' into a process variable, and
        // BusinessRuleTaskExecutionTests asserts a gateway after it takes the path
        // the table chose.
        string[] expectedComingSoon =
        [
            "Data Input", "Data Output", "Lane",
            "Message Flow", "Pool / Participant",
        ];

        static string Diff(string label, IEnumerable<string> actual, IEnumerable<string> expected)
        {
            var added = actual.Except(expected, StringComparer.Ordinal).ToList();
            var gone = expected.Except(actual, StringComparer.Ordinal).ToList();
            if (added.Count == 0 && gone.Count == 0) return "";
            return $"{Environment.NewLine}  {label}:"
                + (added.Count == 0 ? "" : $" NOW {label} — {string.Join(", ", added)};")
                + (gone.Count == 0 ? "" : $" NO LONGER {label} — {string.Join(", ", gone)};");
        }

        var drift = Diff("withdrawn", NamesWith("withdrawn"), expectedWithdrawn)
            + Diff("coming-soon", NamesWith("coming-soon"), expectedComingSoon);

        Assert.True(
            drift.Length == 0,
            "The manifest's withdrawn / coming-soon membership changed." + drift
            + $"{Environment.NewLine}{Environment.NewLine}"
            + "An element moving INTO supported is a claim that an author can configure it and it "
            + "runs. If that is now true, say so here and in the milestone's coverage map — the "
            + "totals alone cannot see this edit (#342).");
    }

    /// <summary>
    /// Every <c>cannot-execute</c> row's <c>reason</c> says something specific (#347).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>reason</c> is load-bearing, not decoration: <c>$fields</c> says
    /// <c>cannot-execute</c> means "Refused at publish, quoting <c>reason</c>",
    /// and Outcome 2 is literally "refused at publish, naming it <b>and saying
    /// why</b>". So <c>reason</c> IS the why.
    /// </para>
    /// <para>
    /// It was completely unguarded. Rewriting it to <c>"Not supported."</c> on five
    /// rows left the whole workflow suite green at 612/612, because the test that
    /// looks like it covers this —
    /// <c>Every_element_the_engine_cannot_run_is_refused_by_name</c> — asserts the
    /// error contains <c>element.Reason</c> <b>read from the same file being
    /// mutated</b>. It moves with the mutation. Only the rows that happened to have
    /// literal assertions elsewhere survived.
    /// </para>
    /// <para>
    /// This pins a distinctive fragment of each, as a literal here. It is
    /// deliberately not the whole sentence — the wording should be free to improve
    /// — but the <em>fact</em> each reason asserts is not, because it is the
    /// evidence a descope actually happened.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_cannot_execute_reason_still_says_what_it_said()
    {
        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();

        // name -> a fragment that cannot survive the reason being replaced.
        //
        // #365 widened this past `cannot-execute`. The five message rows are
        // `engine: executes` / `studio: withdrawn`, so nothing in the slim tier
        // covered them and their restored engine-axis reasons -- restored BY #358, for exactly
        // this reason -- were re-overwritable with the suite green. A guard for
        // unguarded fields that left the fields it had just repaired unguarded.
        var required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Task (Generic)"] = "silent pass-through",
            ["Manual Task"] = "does not wait",
            // Send Task was here until #328 made it authorable again. This dictionary
            // is keyed on the WITHDRAWN set, so a row that returns to supported
            // leaves it -- the test compares the two key sets, which is what makes
            // a silent withdrawal impossible in either direction.
            ["Message Start Event"] = "POST /runtime/process-instances",
            ["Intermediate Throw (Message)"] = "flowable-throw-event-invalid-eventdefinition",
            ["Intermediate Catch (Message)"] = "correlating on a declared process variable",
            ["Message Boundary"] = "interrupting and non-interrupting",
            ["Message End"] = "SENDS NOTHING",

            ["Compensation Start Event"] = "EventSubProcessCompensationStartEventActivityBehavior",
            ["Intermediate Throw (Link)"] = "no link event type",
            ["Intermediate Catch (Link)"] = "no link event type",
            ["Cancel Boundary"] = "#220",
            ["Cancel End"] = "#220",
            ["Transaction"] = "#220",
            ["Boundary Event (None)"] = "#282",
            // Business Rule Task was here until #111 made it executable BY
            // EXPANSION. This dictionary is keyed on the cannot-execute/withdrawn
            // set and the test compares BOTH key sets, so a row that becomes
            // executable must leave -- which is what makes a silent move
            // impossible in either direction.
            ["Loop Marker"] = "#159",
        };

        // Any row whose reason records a FINDING -- the engine cannot run it, or
        // the studio cannot author it. Both are claims someone measured.
        var cannotExecute = elements
            .Where(e => e!["engine"]!.GetValue<string>() == "cannot-execute"
                        || e!["studio"]!.GetValue<string>() == "withdrawn")
            .ToDictionary(
                e => e!["name"]!.GetValue<string>(),
                e => e!["reason"]?.GetValue<string>() ?? "",
                StringComparer.Ordinal);

        // The set itself, so a row silently leaving cannot-execute is caught here
        // rather than only by the tallies.
        Assert.Equal(
            required.Keys.Order(StringComparer.Ordinal),
            cannotExecute.Keys.Order(StringComparer.Ordinal));

        var gutted = required
            .Where(pair => !cannotExecute[pair.Key].Contains(pair.Value, StringComparison.Ordinal))
            .Select(pair => $"{pair.Key}: reason no longer mentions '{pair.Value}' — now \"{cannotExecute[pair.Key]}\"")
            .ToList();

        Assert.True(
            gutted.Count == 0,
            "A cannot-execute element's reason no longer says what it said.\n  "
            + string.Join("\n  ", gutted)
            + "\n\nThis text is quoted to the author at publish and is the whole of "
            + "\"saying why\". If the finding genuinely changed, update this guard too (#347).");
    }

    /// <summary>
    /// The engine split over the 54 in-scope elements, pinned (#375).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This number has been wrong in the coverage claim <b>three times</b>: "52",
    /// then "49", then "46". Each correction was reasoned from a different
    /// artifact — the engine-facts tables, then a coincidentally-equal total over
    /// all 69 rows — and none counted over the 54 in-scope elements the claim is
    /// actually about.
    /// </para>
    /// <para>
    /// The 54 are recoverable exactly: every manifest row except the 14
    /// baseline-supported ones and <c>Boundary Event (None)</c>, which had no row
    /// at milestone open (#282). Derived here rather than restated, so it cannot
    /// drift the way the prose has.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_engine_split_over_the_in_scope_54_is_what_the_claim_says()
    {
        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();

        string[] outsideTheScope =
        [
            "Boundary Event (None)", "End Event (None)", "End Event (Terminate)",
            "Exclusive Gateway (XOR)", "Inclusive Gateway (OR)", "Intermediate Catch (Timer)",
            "Parallel Gateway (AND)", "Script Task", "Sequence Flow", "Service Task (Behavior)",
            "Signal Start Event", "Start Event (None)", "Task (Generic)",
            "Timer Start Event", "User Task",
        ];

        // Every exclusion must name a real row. Without this, a typo silently
        // widens the scope and the split is measured over the wrong set -- which
        // is how the first draft of this guard read 57 instead of 54.
        var allNames = elements.Select(e => e!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var unknown = outsideTheScope.Where(n => !allNames.Contains(n)).ToList();
        Assert.True(unknown.Count == 0, $"these exclusions name no manifest row: {string.Join(", ", unknown)}");

        var inScope = elements
            .Where(e => !outsideTheScope.Contains(e!["name"]!.GetValue<string>(), StringComparer.Ordinal))
            .ToList();

        Assert.Equal(54, inScope.Count);

        int Count(string engine) =>
            inScope.Count(e => e!["engine"]!.GetValue<string>() == engine);

        // "38 of the 54 execute; 9 are BPMN artifacts with no execution semantics
        // by design; 7 cannot execute."
        //
        // Was 43 / 3 / 8 until #325 AC5. Six rows moved from `executes` to
        // `annotation` by owner decision -- Pool / Participant, Lane, Message
        // Flow, Data Store Reference, Data Input, Data Output. Nothing about the
        // engine changed: those six always deployed and were never entered by an
        // instance, and `executes` was recording the first half of that while
        // implying the second. The total is still 54.
        //
        // This is a published coverage claim, so the number moving is the point:
        // a reader who saw "43 execute" and now sees 37 should be able to find
        // out why, and this comment is where they look.
        // #111 moved Business Rule Task from cannot-execute to executes: 37 -> 38
        // and 8 -> 7. It executes BY EXPANSION, like Complex Gateway -- the raw
        // element is refused at DEPLOY (KIE is absent from the image; the DMN
        // engine is not), so publish rewrites the deployed copy into a
        // flowable:type=dmn service task.
        Assert.Equal(38, Count("executes"));
        Assert.Equal(9, Count("annotation"));
        Assert.Equal(7, Count("cannot-execute"));
    }

    /// <summary>
    /// Every reason that records a finding still records one (#374).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Every_cannot_execute_reason_still_says_what_it_said</c> pins a literal
    /// fragment per row, which is strong but only covers the rows someone thought
    /// to add. #365 widened it to `studio: withdrawn` and stopped there, leaving
    /// <b>28</b> `executes`/`supported` rows gutteable: <c>Message End</c>'s
    /// "SENDS NOTHING" was pinned while its structural twin <c>Signal End</c>'s
    /// "RAISES NOTHING" was not, because #340 happened to touch one of them.
    /// </para>
    /// <para>
    /// Three versions of that list have gone stale, so this is the <b>property</b>
    /// instead: a reason exists to record something measured, and the two ways to
    /// destroy one without deleting it are to make it generic
    /// (<c>"Not supported."</c>) or to reduce it to a pointer (<c>"#220"</c>).
    /// Both are caught without naming a single row, so a new element is covered
    /// the day it is added.
    /// </para>
    /// <para>
    /// The literal-fragment test keeps its job for the rows where the exact
    /// finding matters. This is the floor under all 45.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_reason_that_exists_still_records_a_measurement()
    {
        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();

        var gutted = elements
            .Select(e => (Name: e!["name"]!.GetValue<string>(),
                          Reason: (e!["reason"]?.GetValue<string>() ?? "").Trim()))
            .Where(row => row.Reason.Length > 0)
            .Where(row => !ReasonLooksLikeAMeasurement(row.Reason))
            .Select(row => $"{row.Name}: \"{row.Reason}\"")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            gutted.Count == 0,
            "These reasons no longer record a measurement. A reason is quoted to the "
            + "author at publish and is the evidence a finding was made; it must say "
            + "more than a generic phrase or an issue number.\n  "
            + string.Join("\n  ", gutted)
            + "\n\n(#374 — three versions of a per-row literal list went stale, so "
            + "this is the floor under all of them.)");
    }

    /// <summary>The floor can see both ways a reason gets destroyed (#374).</summary>
    /// <remarks>
    /// A property test whose predicate silently stopped matching would report a
    /// clean file forever — the failure this milestone has found more than any
    /// other.
    /// </remarks>
    [Theory]
    [InlineData("Not supported.")]
    [InlineData("#220")]
    [InlineData("")]
    [InlineData("Does not work; see the issue.")]
    // 72 characters, matches on `engine`, and cleared every earlier version of
    // this floor. It is the canonical gutting string repeated to length (#380).
    [InlineData("Not supported by the engine. Not supported by the engine. Not supported.")]
    [InlineData("#163 #163 #163 #163 #163 #163 #163 #163 #163 #163 #163 #163 #163 #163")]
    public void A_gutted_reason_is_not_a_measurement(string gutted)
    {
        // The REAL predicate, not a private copy of it (#380). A meta-test that
        // reimplements what it is checking cannot notice the original drifting --
        // and that is exactly what happened: this passed while the floor it
        // claimed to exercise admitted the string on the last row below.
        Assert.False(
            ReasonLooksLikeAMeasurement(gutted),
            $"\"{gutted}\" would pass the floor");
    }

    /// <summary>
    /// The manifest's own provenance is asserted (#347).
    /// </summary>
    /// <remarks>
    /// The entire file is a claim about one engine version, produced by deploying
    /// to it. Both statements of that could be rewritten — <c>"8.0.0"</c> to
    /// <c>"9.9.9"</c>, <c>generatedFrom</c> to <c>"guessed from memory"</c> — with
    /// 612/612 green.
    /// </remarks>
    [Fact]
    public void The_manifest_still_claims_to_come_from_a_real_engine_run()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!;

        Assert.Equal("8.0.0", manifest["flowableVersion"]?.GetValue<string>());

        var generatedFrom = manifest["generatedFrom"]?.GetValue<string>() ?? "";
        Assert.False(
            string.IsNullOrWhiteSpace(generatedFrom),
            "The manifest no longer says where it came from.");
        Assert.Contains("deploy", generatedFrom, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The guard above can actually fail (#330).
    /// </summary>
    /// <remarks>
    /// A tally test whose expectations are computed from the same file it is
    /// checking passes forever. This asserts the expected numbers are literals
    /// that disagree with a mutated manifest — the property the previous
    /// "guarded going forward" claim lacked.
    /// </remarks>
    [Fact]
    public void Flipping_one_row_changes_a_tally()
    {
        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();
        var supported = elements.Count(e => e!["studio"]!.GetValue<string>() == "supported");

        // Flip the first supported row in an in-memory copy.
        var mutated = elements
            .Select(e => e!["studio"]!.GetValue<string>())
            .ToList();
        var first = mutated.IndexOf("supported");
        Assert.True(first >= 0, "the manifest has no supported rows, which is itself a defect");
        mutated[first] = "withdrawn";

        Assert.NotEqual(supported, mutated.Count(v => v == "supported"));
    }

    // ── The manifest is internally coherent ─────────────────────────────────

    [Fact]
    public void Every_entry_is_complete_and_uses_a_defined_status()
    {
        var studioStatuses = new[]
        {
            BpmnSupportManifest.StudioStatusSupported,
            BpmnSupportManifest.StudioStatusComingSoon,
            BpmnSupportManifest.StudioStatusWithdrawn
        };
        var engineStatuses = new[]
        {
            BpmnSupportManifest.EngineExecutes,
            BpmnSupportManifest.EngineAnnotation,
            BpmnSupportManifest.EngineCannotExecute
        };

        Assert.NotEmpty(BpmnSupportManifest.Default.Elements);

        foreach (var element in BpmnSupportManifest.Default.Elements)
        {
            Assert.False(string.IsNullOrWhiteSpace(element.Name));
            Assert.False(string.IsNullOrWhiteSpace(element.Category));
            Assert.False(string.IsNullOrWhiteSpace(element.LocalName));
            Assert.Contains(element.Studio, studioStatuses);
            Assert.Contains(element.Engine, engineStatuses);

            // A refusal with no reason is a rejection, not an explanation — the
            // exact failure the third acceptance criterion names.
            if (element.CannotExecute)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(element.Reason),
                    $"'{element.Name}' cannot execute but carries no reason, so the author " +
                    "would be refused without being told why.");
            }
        }
    }

    [Fact]
    public void The_studio_never_advertises_what_the_engine_cannot_run()
    {
        // The manifest's stated invariant, and the one that keeps the two axes
        // honest. "Coming soon" for something the engine runs is fine — that is
        // just work we owe. "Supported" for something it cannot run is the lie
        // this epic exists to end.
        var lying = BpmnSupportManifest.Default.Elements
            .Where(element => element.StudioSupported && element.CannotExecute)
            .Select(element => element.Name)
            .ToArray();

        Assert.Empty(lying);
    }

    [Fact]
    public void No_two_entries_claim_the_same_bpmn_key()
    {
        // Two rows with the same (localName, eventDefinition) would make Match's
        // answer depend on manifest order, which is exactly the kind of silent
        // ambiguity the old localName-only lists had.
        var duplicates = BpmnSupportManifest.Default.Elements
            .GroupBy(element => (element.LocalName, element.EventDefinition))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} → {string.Join(", ", group.Select(e => e.Name))}")
            .ToArray();

        Assert.Empty(duplicates);
    }

    // ── One source: the SPA and the backend read the same bytes ─────────────

    [Fact]
    public void The_embedded_manifest_is_the_shared_file_byte_for_byte()
    {
        // If these ever diverge, "one source of truth" is a comment rather than a
        // fact: the studio would advertise from one copy while publish validation
        // refused from another.
        var onDisk = File.ReadAllText(SharedManifestPath);
        var embedded = BpmnSupportManifest.ReadEmbeddedJson();

        Assert.Equal(
            onDisk.ReplaceLineEndings("\n"),
            embedded.ReplaceLineEndings("\n"),
            ignoreLineEndingDifferences: false);
    }

    [Fact]
    public void The_studio_derives_its_element_list_rather_than_declaring_one()
    {
        // The regression this guards is precise and has happened once already: a
        // second list declared next to the consumer, correct on the day it was
        // written. `SUPPORTED_BPMN_TYPES` and `COMING_SOON_BPMN_TYPES` were that
        // list, and #103 found them wrong about 47 of 68 elements.
        var studio = File.ReadAllText(StudioPath);

        Assert.DoesNotContain("SUPPORTED_BPMN_TYPES: BpmnTypeGroup[]", studio, StringComparison.Ordinal);
        Assert.DoesNotContain("COMING_SOON_BPMN_TYPES: BpmnTypeGroup[]", studio, StringComparison.Ordinal);
        Assert.Contains("from \"@/lib/bpmn/support\"", studio, StringComparison.Ordinal);

        // And that module must read the shared file, not a copy of it.
        var support = File.ReadAllText(SpaSupportModulePath);
        Assert.Contains("@shared/bpmn-support.json", support, StringComparison.Ordinal);
    }

    [Fact]
    public void The_types_panel_names_no_element_of_its_own()
    {
        // The subtler shape of the same drift: not a whole list, but one element
        // name hard-coded into the panel — the way a "temporarily" hidden entry or
        // a one-off footnote gets added. Everything the panel names must have come
        // from the manifest.
        //
        // Scoped to the panel because element names appear legitimately elsewhere
        // in this file: `<Modal title="User Task">` is a property editor's heading,
        // not a claim about support.
        var panel = StripLineComments(TypesPanelSource());

        // As a quoted literal or as JSX text — the two ways a name gets written by
        // hand. A bare substring search is not usable here: Mantine's own `Group`
        // component shares a name with the BPMN element.
        var hardCoded = BpmnSupportManifest.Default.Elements
            .Select(element => element.Name)
            .Where(name => panel.Contains($"\"{name}\"", StringComparison.Ordinal)
                || panel.Contains($">{name}<", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(hardCoded);
    }

    /// <summary>The source of <c>BpmnTypesModal</c>, up to the next top-level declaration.</summary>
    private static string TypesPanelSource()
    {
        var studio = File.ReadAllText(StudioPath);
        var start = studio.IndexOf("function BpmnTypesModal(", StringComparison.Ordinal);
        Assert.True(start >= 0, "BpmnTypesModal is gone; this guard no longer guards anything.");

        var end = studio.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        return end < 0 ? studio[start..] : studio[start..end];
    }

    [Fact]
    public void The_engine_axis_agrees_with_the_inventory_or_declares_why_not()
    {
        // The `engine` field claims to be #103's measurement. Nothing enforced
        // that, and "assignment is not delivery" is the lesson this milestone has
        // already learned once: a manifest can carry a value that quietly
        // contradicts the evidence it cites, and no reader would know.
        //
        // `rows.json` is #103's *corrected* table — the raw probe reclassified
        // where a failure turned out to be a fixture limitation rather than an
        // engine gap. Departures from it are legitimate but must be deliberate,
        // so each one is named here with its reason. An undeclared departure fails.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["executes"] = BpmnSupportManifest.EngineExecutes,
            ["fails at deployment"] = BpmnSupportManifest.EngineCannotExecute,
            ["DEPLOYS BUT DOES NOTHING"] = BpmnSupportManifest.EngineCannotExecute,
        };

        // Declared departures. Keep this list short and argued; a growing list is
        // the signal that the manifest has stopped deriving from the evidence.
        // EVERY DEPARTURE CARRIES A REASON (#464).
        //
        // This was `Dictionary<string, string>` — name to engine value — and
        // every justification was a C# comment, which nothing checks. Measured:
        // setting rows.json's User Task verdict to "DEPLOYS BUT DOES NOTHING"
        // and reconciling it with ONE uncommented line left 807 backend tests
        // green. That is strictly stronger than the forgery #458 closed, because
        // the manifest is never touched: no tally moves, no bucket pin fires, no
        // reason baseline changes.
        //
        // #458 filed this as a smaller finding and #461 did not address it.
        var declared = new Dictionary<string, (string Engine, string Why)>(StringComparer.Ordinal)
        {
            // BPMN artifacts deploy and do nothing BY DESIGN, so "executes" is
            // technically right and useless. The third value exists to say so.
            ["Text Annotation"] = (BpmnSupportManifest.EngineAnnotation, "A BPMN artifact: deploys and carries no execution semantics by design."),
            ["Group"] = (BpmnSupportManifest.EngineAnnotation, "A BPMN artifact: deploys and carries no execution semantics by design."),
            ["Association"] = (BpmnSupportManifest.EngineAnnotation, "A BPMN artifact: deploys and carries no execution semantics by design."),

            // #325 AC5, by owner decision. Six more rows in the same position as
            // the three above: they deploy, and no instance ever ENTERS them, so
            // no run can prove them. AC5 offered two outcomes -- gain evidence,
            // or move to cannot-execute with a reason -- and neither fits: the
            // first is impossible and the second means refusing a Lane at
            // publish, which would be a real regression. `annotation` is the
            // word this manifest already has for it.
            //
            // Data OBJECT Reference is deliberately NOT here. It creates a real
            // runtime process variable and `DataObjectExecutionTests` already
            // proves it by starting an instance; the obstacle is that the
            // execution oracle's entry assertion is activity-shaped, which is
            // work rather than a reclassification.
            ["Pool / Participant"] = (BpmnSupportManifest.EngineAnnotation, "#325 AC5: deploys, and no instance ever enters it, so no run can prove it."),
            ["Lane"] = (BpmnSupportManifest.EngineAnnotation, "#325 AC5: deploys, and no instance ever enters it, so no run can prove it."),
            ["Message Flow"] = (BpmnSupportManifest.EngineAnnotation, "#325 AC5: deploys, and no instance ever enters it, so no run can prove it."),
            ["Data Store Reference"] = (BpmnSupportManifest.EngineAnnotation, "#325 AC5: a design-time declaration creating no runtime variable; no instance enters it."),
            ["Data Input"] = (BpmnSupportManifest.EngineAnnotation, "#325 AC5: a design-time declaration creating no runtime variable; no instance enters it."),
            ["Data Output"] = (BpmnSupportManifest.EngineAnnotation, "#325 AC5: a design-time declaration creating no runtime variable; no instance enters it."),

            // "needs configuration" is not a verdict the engine axis has, because
            // it is not a property of the element — it is a property of the
            // diagram. The two cases split on whether an author can fix it:
            //
            //   Send Task      — an author supplies `type`/`operationRef` and it
            //                    runs. Author-fixable, so: executes.
            ["Send Task"] = (BpmnSupportManifest.EngineExecutes, "An author supplies type/operationRef and it runs, so the gap is configuration, not the engine."),

            // #111. This entry read `cannot-execute`, "the DMN engine is absent
            // from the image ... until #105". Two corrections, both measured.
            //
            // The DMN engine is NOT absent. #106 proved it ships enabled on this
            // image and answers on /flowable-rest/dmn-api/. What is absent is
            // KIE/Drools — a different engine — and the old reason conflated them.
            //
            // And the failure is at DEPLOY, not at run time. BusinessRuleParseHandler
            // has exactly one path and resolves the KIE behaviour while parsing, so
            // a raw bpmn:businessRuleTask is HTTP 500 before any instance exists.
            // `flowable:type="dmn"` is never consulted on the element;
            // createDmnActivityBehavior takes a ServiceTask.
            //
            // So the departure is the same shape as Complex Gateway's below: the
            // element executes by EXPANSION. Publish rewrites the deployed copy into
            // a DMN service task and the authored diagram keeps what was drawn.
            // Probed end to end first: same table, same field extension, one element
            // name changed, and the expanded form wrote route='big' into a variable.
            ["Business Rule Task"] = (BpmnSupportManifest.EngineExecutes, "#111: the raw element is refused at DEPLOY (NoClassDefFoundError org/kie/api -- KIE is absent, the DMN engine is not), so publish expands it into a flowable:type=dmn service task. Executes by expansion."),

            // #218. rows.json records "DEPLOYS BUT DOES NOTHING", and that
            // verdict is wrong in a way worth stating rather than quietly
            // overwriting.
            //
            // Re-probed against 8.0.0 before this story was implemented:
            // a complexGateway is recorded in history as activityType
            // exclusiveGateway, it EVALUATES conditionExpression on its outgoing
            // flows, it honours `default`, and with two conditions true it takes
            // the first match. It is not inert — it silently picks a branch. The
            // inventory's claim holds only for the element's own
            // `activationCondition`, which Flowable never evaluates because there
            // is no ComplexGatewayActivityBehavior.
            //
            // The departure is that publish no longer deploys the raw element: an
            // author's routing script goes in front of it and its routes are
            // conditioned on the result. Spike #155 is not contradicted — it
            // proved no extension point reaches the element, which is why this is
            // done by expansion rather than by a behaviour.
            ["Complex Gateway"] = (BpmnSupportManifest.EngineExecutes, "#218: re-probed against 8.0.0 -- it evaluates conditions, honours default, and picks a branch. Not inert; the routing is done by expansion. #231: the expansion now has TWO shapes -- one routing script for a gateway with one incoming flow, and one accumulator per incoming flow for a gateway with more, which is what lets a join see which branches have arrived and fire once."),

            // #159. rows.json says "executes". It does not.
            //
            // Measured against a control on the same task: every spelling of
            // standardLoopCharacteristics ran the activity ONCE — loopCondition
            // ${true} with loopMaximum=3, ${loopCounter < 3}, and testBefore —
            // while multiInstanceLoopCharacteristics with cardinality 3 on that
            // same task ran it three times. The control is what makes this a
            // finding rather than a mis-set marker.
            //
            // The departure is downward, which is rarer and worth stating
            // plainly: the inventory credited the engine with a capability it
            // does not have, and an author marking a task as a loop got one that
            // runs once. Publish now refuses it.
            ["Loop Marker"] = (BpmnSupportManifest.EngineCannotExecute, "A downward departure: the inventory credited a capability the engine lacks -- a task marked as a loop ran once. Publish refuses it."),

            // #103 probed this at process level, where Flowable rejects it
            // (flowable-start-event-invalid-event-definition). Inside an event
            // subprocess it runs — EventSubProcessConditionalStartEventActivityBehavior
            // ships. So the element is not the problem; where the studio lets you
            // put it is, and the studio currently offers it as a process start.
            //
            // Recorded on #158, which owns conditional events, and #162, which
            // owns event subprocesses. Marking it cannot-execute here would be
            // wrong in the other direction and would block #162.
            ["Conditional Start Event"] = (BpmnSupportManifest.EngineExecutes, "#158/#162: correct inside an event subprocess, refused at process level. cannot-execute here would be wrong in the other direction and would block #162."),

            // #112. The inventory is right about the raw element: Flowable 8.0.0
            // rejects an intermediate throw carrying a message definition at
            // deployment —
            //   [Validation set: 'flowable-executable-process'
            //    | Problem: 'flowable-throw-event-invalid-eventdefinition']
            //   Unsupported intermediate throw event type
            // — which is why rows.json records "fails at deployment".
            //
            // The departure is that publish no longer deploys the raw element.
            // WorkflowBpmnXml.ExpandForDeployment rewrites it into a service task
            // on the send behaviour, so what reaches the engine is something the
            // engine runs, and an author's diagram does what it says. Asserted
            // end-to-end in MessageCorrelationExecutionTests.
            //
            // This is the one departure of its kind: a verdict overturned by
            // changing what we deploy rather than by the engine changing. If a
            // second one appears, the honest move is a field on the manifest
            // saying "executes after expansion", not a longer list here.
            ["Intermediate Throw (Message)"] = (BpmnSupportManifest.EngineExecutes, "#112: the verdict was overturned by changing what we deploy, not by the engine changing -- the rewrite makes it a service task that sends."),

            // #156. The same shape as the message end event, and found the same
            // way — by testing the element instead of trusting the inventory. A
            // signal end event deploys and ends the process, so #103's probe
            // recorded "executes"; it raises no signal at all, which a catcher
            // waiting on the name proved by never firing. Publish rewrites it into
            // an intermediate throw plus a terminal end event.
            //
            // Declared rather than silently edited, because the inventory's
            // verdict is defensible for what it measured — the element runs — and
            // the departure is that it does not do the one thing it exists for.
            ["Signal End"] = (BpmnSupportManifest.EngineExecutes, "#156: the inventory's verdict is defensible for what it measured -- the element runs -- and the departure is that it did not do the one thing it exists for."),

            // #220 / #228. The inventory's "executes" is right about what it
            // measured — all three deploy and a transaction subprocess runs — and
            // wrong about the only thing they exist for. The BPMN rollback idiom
            // (cancel end inside, cancel boundary outside) fails at RUNTIME on
            // Flowable 8.0.0: the boundary cannot resolve the transaction's
            // execution, and one path corrupts act_ru_execution with a foreign-key
            // violation. That is an engine defect, not a modelling error.
            //
            // Marked cannot-execute and withdrawn. Compensation is deliberately
            // NOT included here: probed separately, it works, so #115 stands on
            // its own — the two are usually described together and it would be
            // easy to withdraw both by association.
            ["Cancel Boundary"] = (BpmnSupportManifest.EngineCannotExecute, "Requires a transaction subprocess, which Flowable 8.0.0 does not run; measured at deployment."),
            ["Cancel End"] = (BpmnSupportManifest.EngineCannotExecute, "Requires a transaction subprocess, which Flowable 8.0.0 does not run; measured at deployment."),
            ["Transaction"] = (BpmnSupportManifest.EngineCannotExecute, "Flowable 8.0.0 does not run transaction subprocesses; measured at deployment."),
        };

        var rows = JsonNode.Parse(File.ReadAllText(InventoryRowsPath))!.AsArray();
        var manifest = BpmnSupportManifest.Default.Elements.ToDictionary(e => e.Name, StringComparer.Ordinal);

        Assert.Equal(manifest.Count, rows.Count);

        var undeclared = new List<string>();
        foreach (var row in rows)
        {
            var name = (string)row!["name"]!;
            var verdict = (string)row["verdict"]!;
            var element = Assert.Contains(name, manifest);

            var want = declared.TryGetValue(name, out var departure)
                ? departure.Engine
                : expected.GetValueOrDefault(verdict);

            if (want is null)
            {
                undeclared.Add($"{name}: inventory verdict '{verdict}' maps to nothing, and no departure is declared");
            }
            else if (element.Engine != want)
            {
                undeclared.Add($"{name}: inventory says '{verdict}' (expected engine '{want}'), manifest says '{element.Engine}'");
            }
        }

        Assert.Empty(undeclared);

        // A departure naming no row is silently ignored by the loop above, so it
        // reads as justification for nothing (#464).
        var orphans = declared.Keys
            .Where(name => !rows.Any(r => (string)r!["name"]! == name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "These declared departures name no row in the inventory, so they justify nothing:\n  "
            + string.Join("\n  ", orphans));

        var unexplained = declared
            .Where(d => string.IsNullOrWhiteSpace(d.Value.Why))
            .Select(d => d.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "These departures from the measured inventory carry no reason:\n  "
            + string.Join("\n  ", unexplained)
            + "\n\nA departure is a claim that the probe's verdict is wrong. Saying so in a C# "
            + "comment is not saying so to a test (#464).");
    }

    // ── One source: publish validation follows the manifest, not a list ─────

    [Fact]
    public void Every_element_the_engine_cannot_run_is_refused_by_name()
    {
        // Driven from the manifest rather than from a fixed list of six, so an
        // element added to the manifest tomorrow is covered by this test today.
        // A hand-written carve-out in the validator — the pattern the old code
        // had grown — shows up here as a failure.
        foreach (var element in BpmnSupportManifest.Default.Elements.Where(e => e.CannotExecute))
        {
            var result = WorkflowBpmnXml.ValidateProcess(ProcessContaining(element));

            Assert.Contains(
                result.Errors,
                error => error.Contains(element.Name, StringComparison.Ordinal));

            // The refusal explains itself. A message that names the element but
            // not the cause tells an author what to delete, not what to do.
            Assert.Contains(
                result.Errors,
                error => error.Contains(element.Reason!, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Nothing_the_engine_runs_is_refused_unless_the_studio_withdrew_it()
    {
        // The complement, and the half that catches the bug #103 found: the old
        // deny-lists keyed on localName alone, so denying `boundaryEvent` blocked
        // all eight variants although Flowable executes seven. A test that only
        // asserted refusals would pass for a validator that refuses everything.
        //
        // **Widened for #167.** Manual Task and Task (Generic) keep `engine:
        // executes` — the engine really does run them, straight through, without
        // waiting for anyone — and are refused at publish anyway, because running
        // into silence is the failure this milestone exists to end. So the rule is
        // "nothing the engine runs is refused UNLESS the studio withdrew it", and
        // the withdrawn set is the exemption. Wording those refusals to dodge the
        // substring this test greps for would have been evasion rather than a fix.
        foreach (var element in BpmnSupportManifest.Default.Elements
                     .Where(e => !e.CannotExecute && e.Studio != BpmnSupportManifest.StudioStatusWithdrawn))
        {
            var result = WorkflowBpmnXml.ValidateProcess(ProcessContaining(element));

            Assert.DoesNotContain(
                result.Errors,
                error => error.Contains("cannot be deployed", StringComparison.Ordinal)
                    && error.Contains(element.Name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Flipping_one_element_in_the_manifest_changes_what_publish_accepts()
    {
        // The acceptance criterion asks for a test that flips one element and
        // asserts the consumers follow. This is that test for the backend
        // consumer, run against the real validation path with a perturbed
        // manifest — so it proves the behaviour is a function of the manifest,
        // not that the two happen to agree today.
        //
        // The SPA consumer is covered structurally instead
        // (The_studio_derives_its_element_list_rather_than_declaring_one), because
        // it derives its list by mapping over these same bytes at build time; a
        // runtime flip has nothing to reach.
        const string subject = "User Task";
        var element = BpmnSupportManifest.Default.Elements.Single(e => e.Name == subject);
        var xml = ProcessContaining(element);

        // Baseline: accepted today.
        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            error => error.Contains(subject, StringComparison.Ordinal));

        var flipped = BpmnSupportManifest.Parse(
            FlipToCannotExecute(BpmnSupportManifest.ReadEmbeddedJson(), subject, "flipped by a test"));

        var refused = WorkflowBpmnXml.ValidateProcess(xml, flipped).Errors;

        Assert.Contains(refused, error => error.Contains(subject, StringComparison.Ordinal));
        Assert.Contains(refused, error => error.Contains("flipped by a test", StringComparison.Ordinal));

        // And the embedded manifest is untouched by the perturbation, so the
        // flip proves a dependency rather than leaking into the other tests.
        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            error => error.Contains(subject, StringComparison.Ordinal));
    }

    [Fact]
    public void An_activity_carrying_a_marker_is_still_judged_on_the_activity()
    {
        // Match returns every entry describing a node, not the first. A business
        // rule task with a multi-instance marker has two descriptions and only one
        // of them cannot run; a first-match lookup in manifest order answers
        // "Multi-Instance (Parallel), executes" and lets it through.
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                    targetNamespace="http://autonate.dev/workflows">
                    <bpmn:process id="p" isExecutable="true">
                      <bpmn:startEvent id="s" />
                      <bpmn:transaction id="rules" name="Score it">
                        <bpmn:multiInstanceLoopCharacteristics isSequential="false" />
                      </bpmn:transaction>
                      <bpmn:endEvent id="e" />
                    </bpmn:process>
                  </bpmn:definitions>
                  """;

        var errors = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        // Rebased off Business Rule Task by #111, which made that element
        // executable. Transaction is the durable stand-in: `withdrawn` as well as
        // `cannot-execute`, so no story is queued to make it run.
        Assert.Contains(errors, error => error.Contains("Transaction", StringComparison.Ordinal));
    }

    // ── #160: link events ───────────────────────────────────────────────────

    [Fact]
    public void A_link_event_is_refused_without_any_engine_present()
    {
        // AC2's test plan asks for proof that the refusal fires on the raw XML
        // rather than on a parsed Flowable model. This test IS that proof, and it
        // is worth saying why rather than leaving it implied.
        //
        // `flowable-bpmn-model-8.0.0.jar` carries no link event type, so the XML
        // converter discards the element before validation ever runs. A check
        // written against a parsed model would therefore find nothing and pass for
        // a validator that does nothing at all — the shape #217 established.
        //
        // `ValidateProcess` reads the submitted string with XDocument and has no
        // Flowable dependency; this test class boots no engine and reaches no
        // network. So a refusal here can only have come from the XML itself.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="p" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:intermediateThrowEvent id="jump_out" name="Go to review">
                                 <bpmn:linkEventDefinition id="l1" name="Review" />
                               </bpmn:intermediateThrowEvent>
                               <bpmn:intermediateCatchEvent id="jump_in" name="Review">
                                 <bpmn:linkEventDefinition id="l2" name="Review" />
                               </bpmn:intermediateCatchEvent>
                               <bpmn:endEvent id="e" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var errors = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        // Both halves of the pair are named — refusing only the throw would leave
        // an author deleting one and hitting the same wall.
        var thrown = Assert.Single(errors, e => e.Contains("Intermediate Throw (Link)", StringComparison.Ordinal));
        var caught = Assert.Single(errors, e => e.Contains("Intermediate Catch (Link)", StringComparison.Ordinal));

        Assert.Contains("Go to review", thrown, StringComparison.Ordinal);
        Assert.Contains("Review", caught, StringComparison.Ordinal);

        // AC3: the message names what to use instead. "Cannot be deployed" alone
        // leaves an author with a diagram and no way forward.
        Assert.Contains("sequence flow", thrown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sequence flow", caught, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_process_that_merely_mentions_link_events_still_publishes()
    {
        // The false-positive guard. `linkEventDefinition` is substring-friendly:
        // a documentation annotation explaining why link events are unavailable
        // would be refused by a naive text search, which is a memorable way to
        // make the refusal itself unusable.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="p" isExecutable="true">
                               <bpmn:startEvent id="s" />
                               <bpmn:userTask id="t" name="Approve" />
                               <bpmn:endEvent id="e" />
                               <bpmn:textAnnotation id="note">
                                 <bpmn:text>No linkEventDefinition here — use a sequence flow.</bpmn:text>
                               </bpmn:textAnnotation>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            error => error.Contains("cannot be deployed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_studio_offers_no_link_events()
    {
        // AC1. Asserted on the manifest because that is now the only place the
        // panel could get them from; #107's E2E covers the rendering half.
        var links = BpmnSupportManifest.Default.Elements
            .Where(element => element.EventDefinition == "link")
            .ToArray();

        Assert.Equal(2, links.Length);
        Assert.All(links, element =>
            Assert.Equal(BpmnSupportManifest.StudioStatusWithdrawn, element.Studio));
    }

    [Fact]
    public void A_refusal_names_the_offending_element_in_the_diagram()
    {
        // "Validation failed" tells an author nothing about which of forty
        // elements to look at.
        //
        // REBASED TWICE NOW, and the pattern is worth naming rather than just
        // fixing again. #218 moved it off a complex gateway when that element
        // started publishing; #111 moves it off Business Rule Task for the same
        // reason. Each story that makes an element executable orphans whatever
        // fixture was using it as "the refused one", and the test stays GREEN
        // while testing nothing -- it would find some other error and match on it.
        //
        // Transaction is the durable choice. It is `withdrawn` on the studio axis
        // as well as `cannot-execute`, so no story is queued to make it run: the
        // element needs a compensation-and-cancel protocol Auton8 does not model,
        // and nothing in M4 or M5 proposes to. A `supported` element that merely
        // has not been built yet is exactly the wrong fixture here, because the
        // story that builds it inherits this breakage.
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                    targetNamespace="http://autonate.dev/workflows">
                    <bpmn:process id="p" isExecutable="true">
                      <bpmn:startEvent id="s" />
                      <bpmn:transaction id="tx" name="Take the payment" />
                      <bpmn:endEvent id="e" />
                    </bpmn:process>
                  </bpmn:definitions>
                  """;

        // Matched on the REFUSAL specifically. An empty transaction also trips the
        // "a subprocess needs a start event" rule, and a predicate of "any error
        // mentioning Transaction" would be satisfied by that one -- which would
        // leave this test green while the refusal it exists for had stopped
        // firing. That is the same shape of silent pass the rebase above is about.
        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("cannot be deployed", StringComparison.Ordinal));

        Assert.Contains("Transaction", error, StringComparison.Ordinal);
        Assert.Contains("Take the payment", error, StringComparison.Ordinal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal process containing one manifest element, built from the
    /// manifest's own key so the test cannot drift from what it is testing.
    /// </summary>
    private static string ProcessContaining(BpmnSupportManifest.Element element)
    {
        var body = element.LocalName == "*"
            ? MarkedActivity(element.EventDefinition!)
            : Node(element);

        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="{Bpmn}"
                                  targetNamespace="http://autonate.dev/workflows">
                  <bpmn:process id="p" isExecutable="true">
                    <bpmn:startEvent id="probe_start" />
                {body}
                    <bpmn:endEvent id="probe_end" />
                  </bpmn:process>
                </bpmn:definitions>
                """;
    }

    private static string Node(BpmnSupportManifest.Element element)
    {
        var name = element.LocalName;
        var definition = element.EventDefinition;

        // The event-subprocess key is an attribute rather than a child.
        if (definition == "triggeredByEvent")
        {
            return $"""    <bpmn:{name} id="probe" name="Probe" triggeredByEvent="true" />""";
        }

        if (definition is null)
        {
            return $"""    <bpmn:{name} id="probe" name="Probe" />""";
        }

        return $"""
                    <bpmn:{name} id="probe" name="Probe">
                      <bpmn:{definition}EventDefinition id="probe_def" />
                    </bpmn:{name}>
            """;
    }

    private static string MarkedActivity(string marker)
    {
        var child = marker switch
        {
            "standardLoopCharacteristics" => """<bpmn:standardLoopCharacteristics />""",
            "multiInstanceLoopCharacteristics:parallel" =>
                """<bpmn:multiInstanceLoopCharacteristics isSequential="false" />""",
            "multiInstanceLoopCharacteristics:sequential" =>
                """<bpmn:multiInstanceLoopCharacteristics isSequential="true" />""",
            "isForCompensation" => null,
            _ => throw new InvalidOperationException($"No probe shape for marker '{marker}'.")
        };

        if (child is null)
        {
            return """    <bpmn:task id="probe" name="Probe" isForCompensation="true" />""";
        }

        return $"""
                    <bpmn:task id="probe" name="Probe">
                      {child}
                    </bpmn:task>
            """;
    }

    // ── #255: the evidence field was the one nothing read ───────────────────

    /// <summary>
    /// The only element whose runtime creates a process variable.
    /// </summary>
    /// <remarks>
    /// Re-probed against Flowable 8.0.0 while fixing #255: a process carrying a
    /// <c>dataStoreReference</c>, an <c>ioSpecification</c> with a
    /// <c>dataInput</c> and a <c>dataOutput</c>, and a <c>dataObject</c>, started
    /// and inspected, reported exactly one variable — the data object's. The
    /// other three are design-time declarations; the condition validator counts
    /// them as known names so a condition using one is not flagged as a typo, and
    /// that is all they do.
    /// </remarks>
    private const string OnlyElementWithARuntimeVariable = "Data Object Reference";

    [Fact]
    public void No_element_claims_a_runtime_variable_it_does_not_create()
    {
        // The manifest's whole design is that a later reader can CHECK a claim
        // rather than trust it, and every other field here is guarded. `evidence`
        // was not read by anything -- so three rows carried
        // "declared type observed on the process variable" for elements that
        // produce no variable at all, added during #166 without measurement, and
        // nothing noticed. `rows.json` had the honest weaker note the whole time.
        var manifest = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!;

        var overclaiming = manifest["elements"]!.AsArray()
            .Where(node => ((string?)node!["evidence"] ?? "")
                .Contains("on the process variable", StringComparison.OrdinalIgnoreCase))
            .Select(node => (string)node!["name"]!)
            .Where(name => name != OnlyElementWithARuntimeVariable)
            .ToList();

        Assert.True(
            overclaiming.Count == 0,
            "Elements whose evidence claims a process variable was observed, when " +
            $"only '{OnlyElementWithARuntimeVariable}' creates one: " +
            string.Join(", ", overclaiming));
    }

    [Fact]
    public void Every_element_carries_evidence_that_says_something()
    {
        // An empty or placeholder evidence string is the same failure one step
        // earlier: a field that reads as measured and is not.
        var manifest = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!;

        foreach (var node in manifest["elements"]!.AsArray())
        {
            var name = (string)node!["name"]!;
            var evidence = (string?)node["evidence"];

            Assert.False(
                string.IsNullOrWhiteSpace(evidence),
                $"'{name}' has no evidence. Every row in this manifest was measured " +
                "against a running engine; a row that cannot say how is a guess.");

            Assert.True(
                evidence!.Length > 10,
                $"'{name}' has evidence of '{evidence}', which is too short to " +
                "record what was actually run.");

            Assert.DoesNotContain("TODO", evidence, StringComparison.OrdinalIgnoreCase);
        }
    }

    // A third guard was tried here and removed: "manifest evidence must begin
    // with the rows.json note". It failed on its first run against
    // 'Intermediate Throw (None)', whose evidence reads "deployed and executed"
    // where the note says "deployed and started" -- a stronger and entirely
    // honest wording. The field records seventeen different kinds of probe
    // (deployments, missing behaviour classes, a NoClassDefFoundError, a
    // re-probe that corrected an earlier one), and a rule that needs a carve-out
    // on day one is a rule that gets weakened later. The two above catch what
    // #255 actually was: a claim about a runtime artefact that does not exist.

    private static string FlipToCannotExecute(string json, string name, string reason)
    {
        var root = JsonNode.Parse(json)
            ?? throw new InvalidOperationException("The manifest JSON did not parse.");
        var elements = root["elements"]!.AsArray();
        var target = elements.Single(node => (string?)node!["name"] == name)!;
        target["engine"] = BpmnSupportManifest.EngineCannotExecute;
        target["studio"] = BpmnSupportManifest.StudioStatusComingSoon;
        target["reason"] = reason;
        return root.ToJsonString();
    }

    /// <summary>
    /// Strips `//` line comments so the hard-coded-name check reads code rather
    /// than prose — the panel's comments legitimately quote element names when
    /// explaining what changed.
    /// </summary>
    private static string StripLineComments(string source) =>
        string.Join(
            "\n",
            source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>
    /// The predicate the floor uses, exposed so the meta-test exercises it (#380).
    /// </summary>
    /// <remarks>
    /// <c>A_gutted_reason_is_not_a_measurement</c> used to build its OWN copy of
    /// the regex and its own <c>&gt;= 60</c>. That made it a tautology over four
    /// hand-picked strings: it could not notice the real floor drifting, and it
    /// did not, which is how the floor shipped admitting the one string it names.
    /// </remarks>
    internal static bool ReasonLooksLikeAMeasurement(string reason)
    {
        var text = reason.Trim();
        if (text.Length < ReasonSubstance) return false;
        if (!ReasonEvidence.IsMatch(text)) return false;

        // #380: the floor's blind spot was repetition. A sentence can clear any
        // length by saying the same thing three times, and "Not supported by the
        // engine." repeated is 72 characters that match on `engine` -- literally
        // the string the floor was written to reject. Real reasons run 0.75 to
        // 1.00 distinct; that one is 0.40.
        var words = Regex.Matches(text, "[A-Za-z][A-Za-z0-9]{3,}")
            .Select(m => m.Value.ToLowerInvariant())
            .ToList();
        if (words.Count == 0) return false;
        var distinct = words.Distinct(StringComparer.Ordinal).Count();
        return (double)distinct / words.Count >= ReasonVariety;
    }

    private static readonly Regex ReasonEvidence = new(
        @"#\d+|Flowable|8\.0\.0|verified|measured|deployed|REST|engine",
        RegexOptions.IgnoreCase);

    /// <summary>The shortest real reason is 71 characters.</summary>
    private const int ReasonSubstance = 60;

    /// <summary>The least varied real reason runs 0.75 distinct; the gutting string, 0.40.</summary>
    private const double ReasonVariety = 0.6;

    private static string ReasonBaselinePath => Path.Combine(
        RepoRoot.Path, "tests", "AutoNate.Web.Tests", "Workflow", "bpmn-reason-baseline.tsv");

    /// <summary>
    /// Every reason is the one that was measured, byte for byte (#380).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half no pattern can do. A floor asks whether a string is
    /// SHAPED like a measurement; four versions of that question have now been
    /// answered "yes" by a string that was not one. This asks whether it is the
    /// string that was actually measured against Flowable 8.0.0 — which is the
    /// property the manifest's whole existence rests on.
    /// </para>
    /// <para>
    /// A reason SHOULD change when someone re-measures, or tightens wording. Then
    /// this file changes in the same commit and a reviewer sees both halves. What
    /// cannot happen any more is 45 reasons quietly becoming noise while the
    /// suite stays green, which is what #353, #365 and #374 each failed to stop.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_reason_is_still_the_one_that_was_measured()
    {
        var baseline = File.ReadAllLines(ReasonBaselinePath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('\t'))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        Assert.True(baseline.Count > 40,
            $"The reason baseline holds {baseline.Count} rows. If it was emptied, this "
            + "guard checks nothing (#380).");

        var actual = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray()
            .Select(e => (Name: e!["name"]!.GetValue<string>(),
                          Reason: (e!["reason"]?.GetValue<string>() ?? "").Trim()))
            .Where(row => row.Reason.Length > 0)
            .ToList();

        var drifted = new List<string>();

        foreach (var (name, reason) in actual)
        {
            var digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(reason)))[..16].ToLowerInvariant();

            if (!baseline.TryGetValue(name, out var expected))
            {
                drifted.Add($"{name}: has a reason with no baseline row");
            }
            else if (!string.Equals(expected, digest, StringComparison.Ordinal))
            {
                drifted.Add($"{name}: reason changed (baseline {expected}, now {digest})");
            }
        }

        foreach (var name in baseline.Keys.Except(actual.Select(a => a.Name), StringComparer.Ordinal))
        {
            drifted.Add($"{name}: baseline row exists but the element has no reason any more");
        }

        Assert.True(
            drifted.Count == 0,
            "These manifest reasons no longer match the measurement they were taken "
            + "from:\n  "
            + string.Join("\n  ", drifted.Order(StringComparer.Ordinal))
            + "\n\nIf you changed a reason DELIBERATELY -- re-measured, or tightened the "
            + "wording -- regenerate tests/AutoNate.Web.Tests/Workflow/bpmn-reason-baseline.tsv "
            + "in the same commit and say why. That is the point: the change becomes "
            + "visible instead of silent (#380).");
    }

    /// <summary>The baseline can be regenerated from the manifest (#380).</summary>
    /// <remarks>
    /// Not a test of the product — it is the recipe, executable so it cannot go
    /// stale, and a check that the two file formats still line up. Set
    /// <c>AUTONATE_REGENERATE_REASON_BASELINE=1</c> to rewrite the file.
    /// </remarks>
    [Fact]
    public void The_reason_baseline_is_regenerable()
    {
        var rows = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray()
            .Select(e => (Name: e!["name"]!.GetValue<string>(),
                          Reason: (e!["reason"]?.GetValue<string>() ?? "").Trim()))
            .Where(row => row.Reason.Length > 0)
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .Select(row => row.Name + "\t" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(row.Reason)))[..16].ToLowerInvariant())
            .ToList();

        Assert.NotEmpty(rows);

        if (Environment.GetEnvironmentVariable("AUTONATE_REGENERATE_REASON_BASELINE") == "1")
        {
            var header = File.ReadAllLines(ReasonBaselinePath).TakeWhile(l => l.StartsWith('#'));
            File.WriteAllText(ReasonBaselinePath,
                string.Join("\n", header) + "\n" + string.Join("\n", rows) + "\n");
        }

        var onDisk = File.ReadAllLines(ReasonBaselinePath)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        Assert.Equal(rows, onDisk);
    }

    /// <summary>
    /// Exactly these elements are in each non-executing engine bucket (#458).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other guard on this axis <b>counts</b> rows — the engine tally, the
    /// in-scope-54 split, the undeclared ratchet, the evidence file's coverage.
    /// None of them checks <em>which</em> rows, so a 1-for-1 swap between two
    /// buckets walks through all of them.
    /// </para>
    /// <para>
    /// Measured, not hypothesised: inverting the exact fact #325's
    /// reclassification turns on — making the manifest say Data Object Reference
    /// does not execute and Data Input does, with the consistent three-file edit
    /// a real commit would make — left <b>806 backend tests green</b> while the
    /// manifest asserted the opposite of what a live engine had said twenty
    /// minutes earlier.
    /// </para>
    /// <para>
    /// This is the sixth relocation of one forgery: a proof in a file (#408,
    /// #418) → delete the obligation (#429) → delete the diagram (#433) →
    /// spell the deletion differently (#447) → skip the theory (#453) → trade
    /// the obligation. Naming the members is what a count cannot do.
    /// </para>
    /// <para>
    /// `executes` is deliberately the complement rather than a third literal:
    /// it is 51 rows and would be noise, and any move out of it changes one of
    /// the two sets below.
    /// </para>
    /// </remarks>
    [Fact]
    public void Exactly_these_elements_are_annotation_or_cannot_execute()
    {
        string[] annotation =
        [
            "Association",
            "Data Input",
            "Data Output",
            "Data Store Reference",
            "Group",
            "Lane",
            "Message Flow",
            "Pool / Participant",
            "Text Annotation",
        ];

        string[] cannotExecute =
        [
            "Boundary Event (None)",
            // Business Rule Task left this list in #111: it executes by expansion
            // now, like Complex Gateway. This guard exists so a row moving between
            // buckets is a visible edit rather than a tally shifting by one, which
            // is exactly what that move was.
            "Cancel Boundary",
            "Cancel End",
            "Compensation Start Event",
            "Intermediate Catch (Link)",
            "Intermediate Throw (Link)",
            "Loop Marker",
            "Transaction",
        ];

        var elements = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray();

        IReadOnlyList<string> Bucket(string engine) => elements
            .Where(e => e!["engine"]!.GetValue<string>() == engine)
            .Select(e => e!["name"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(annotation, Bucket("annotation"));
        Assert.Equal(cannotExecute, Bucket("cannot-execute"));
    }

    /// <summary>
    /// No row's identity key may move without a deliberate edit here (#464).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every guard on this file pins a row's <em>verdict</em> — which bucket it
    /// is in, what effect it declares, what its reason says. Nothing pinned its
    /// <b>identity</b>: <c>rows.json</c> carries only <c>name</c>, <c>verdict</c>
    /// and <c>note</c>, so nothing tied a name to its
    /// <c>(localName, eventDefinition)</c>.
    /// </para>
    /// <para>
    /// Measured: swapping the <c>eventDefinition</c> of Intermediate Throw
    /// (Signal) and Intermediate Throw (Escalation) — and their diagram arms in
    /// the live-engine oracle — left 247 backend tests and 32 live cells green,
    /// with the oracle reporting "Signal throw runs" having run an escalation
    /// throw.
    /// </para>
    /// <para>
    /// A digest rather than 69 literal triples, for the same reason the reason
    /// baseline is a digest (#380): the list is noise to read and the failure
    /// message says which row moved anyway, because the test recomputes and
    /// diffs. Regenerate it deliberately, and say in the commit WHY an identity
    /// changed — a row's BPMN key changing is a different kind of event from its
    /// verdict changing.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_row_has_changed_its_bpmn_identity()
    {
        // Regenerate with the line this test prints on failure.
        const string Baseline = "a74c316685c57e80";

        var identities = JsonNode.Parse(File.ReadAllText(SharedManifestPath))!["elements"]!.AsArray()
            .Select(e => string.Join('\t',
                e!["name"]!.GetValue<string>(),
                e["localName"]?.GetValue<string>() ?? "None",
                e["eventDefinition"]?.GetValue<string>() ?? "None"))
            .Order(StringComparer.Ordinal)
            .ToList();

        var digest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join('\n', identities))))[..16]
            .ToLowerInvariant();

        Assert.True(
            string.Equals(digest, Baseline, StringComparison.Ordinal),
            $"A row's (name, localName, eventDefinition) changed: baseline {Baseline}, now {digest}. "
            + "If that is deliberate, update the constant in this test in the same commit and say "
            + "why the identity moved — swapping two rows' keys is invisible to every other guard "
            + "here, and was measured leaving 247 backend tests and 32 live cells green (#464).");
    }
}
