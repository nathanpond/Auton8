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
    public void Nothing_the_engine_runs_is_refused()
    {
        // The complement, and the half that catches the bug #103 found: the old
        // deny-lists keyed on localName alone, so denying `boundaryEvent` blocked
        // all eight variants although Flowable executes seven. A test that only
        // asserted refusals would pass for a validator that refuses everything.
        foreach (var element in BpmnSupportManifest.Default.Elements.Where(e => !e.CannotExecute))
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
                      <bpmn:businessRuleTask id="rules" name="Score it">
                        <bpmn:multiInstanceLoopCharacteristics isSequential="false" />
                      </bpmn:businessRuleTask>
                      <bpmn:endEvent id="e" />
                    </bpmn:process>
                  </bpmn:definitions>
                  """;

        var errors = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        Assert.Contains(errors, error => error.Contains("Business Rule Task", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refusal_names_the_offending_element_in_the_diagram()
    {
        // "Validation failed" tells an author nothing about which of forty
        // elements to look at.
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                    targetNamespace="http://autonate.dev/workflows">
                    <bpmn:process id="p" isExecutable="true">
                      <bpmn:startEvent id="s" />
                      <bpmn:complexGateway id="cg" name="Wait for two of three" />
                      <bpmn:endEvent id="e" />
                    </bpmn:process>
                  </bpmn:definitions>
                  """;

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("Complex Gateway", StringComparison.Ordinal));

        Assert.Contains("Wait for two of three", error, StringComparison.Ordinal);
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
}
