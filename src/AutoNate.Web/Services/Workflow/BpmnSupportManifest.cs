using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace AutoNate.Web.Services.Workflow;

// The one source of truth for what the workflow studio offers and what the engine
// will run (#107).
//
// Before this, three lists were maintained in parallel — the studio's BPMN types
// modal, the palette, and the `UnsupportedRuntime*` sets in WorkflowBpmnXml — and
// nothing failed when one was missed. #103 measured the disagreement by deploying
// all 68 entries to a running Flowable 8.0.0: 22 elements were advertised as
// "coming soon" that nothing stopped from deploying, and 25 were denied at runtime
// that the engine runs perfectly well.
//
// **Keyed on (localName, eventDefinition), and that is the point.** The old
// deny-lists keyed on localName alone, so denying `boundaryEvent` blocked all
// eight boundary variants although Flowable executes seven of them — and the code
// had grown hand-written carve-outs ("timer intermediate catch events are
// first-class") to compensate. Collapsing three coarse lists into one coarse list
// would have satisfied "one source of truth" and kept the bug.
//
// The SPA imports the same JSON file this embeds, so the two cannot drift.
//
// This is an instance type with a <see cref="Default"/> rather than a static class,
// so a test can <see cref="Parse"/> a manifest with one element flipped and drive
// the real validation path with it. Without that seam, "flip one element and every
// consumer follows" could only be asserted by reading the code and believing it.
public sealed class BpmnSupportManifest
{
    private const string ResourceName = "AutoNate.Web.bpmn-support.json";
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>What the studio advertises. Drives the BPMN types panel, nothing else.</summary>
    public const string StudioStatusSupported = "supported";

    public const string StudioStatusComingSoon = "coming-soon";
    public const string StudioStatusWithdrawn = "withdrawn";

    /// <summary>What Flowable does with it. Drives publish validation, nothing else.</summary>
    public const string EngineExecutes = "executes";

    public const string EngineAnnotation = "annotation";
    public const string EngineCannotExecute = "cannot-execute";

    /// <summary>
    /// One row of the manifest. <c>Studio</c> and <c>Engine</c> are deliberately
    /// independent axes: what the studio offers and what the engine runs are
    /// different questions, and #103 found them answered wrongly in both directions
    /// at once. Collapsing them into a single "supported" flag would reintroduce
    /// exactly that conflation.
    /// </summary>
    public sealed record Element(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("studio")] string Studio,
        [property: JsonPropertyName("engine")] string Engine,
        [property: JsonPropertyName("localName")] string LocalName,
        [property: JsonPropertyName("eventDefinition")] string? EventDefinition,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("evidence")] string? Evidence)
    {
        /// <summary>Flowable, as deployed, will not run this. Publish must refuse it.</summary>
        public bool CannotExecute => Engine == EngineCannotExecute;

        /// <summary>The studio advertises this as working today.</summary>
        public bool StudioSupported => Studio == StudioStatusSupported;
    }

    private sealed record Root(
        [property: JsonPropertyName("flowableVersion")] string FlowableVersion,
        [property: JsonPropertyName("elements")] IReadOnlyList<Element> Elements);

    private readonly Root _root;

    private BpmnSupportManifest(Root root) => _root = root;

    /// <summary>The manifest embedded in this assembly — the same bytes the SPA imports.</summary>
    public static BpmnSupportManifest Default { get; } = LoadEmbedded();

    /// <summary>The raw manifest JSON, for tests that need to compare or perturb it.</summary>
    public static string ReadEmbeddedJson()
    {
        using var stream = OpenEmbedded();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static BpmnSupportManifest Parse(string json) =>
        new(JsonSerializer.Deserialize<Root>(json)
            ?? throw new InvalidOperationException("The BPMN support manifest could not be parsed."));

    private static BpmnSupportManifest LoadEmbedded()
    {
        using var stream = OpenEmbedded();
        return new BpmnSupportManifest(
            JsonSerializer.Deserialize<Root>(stream)
            ?? throw new InvalidOperationException("The BPMN support manifest could not be parsed."));
    }

    private static Stream OpenEmbedded() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException(
            $"The BPMN support manifest '{ResourceName}' is not embedded. It is the source " +
            "the types panel and publish validation both derive from; without it the " +
            "application cannot say which elements it can execute.");

    public string FlowableVersion => _root.FlowableVersion;

    public IReadOnlyList<Element> Elements => _root.Elements;

    /// <summary>
    /// Every manifest entry that describes a BPMN node — usually one, but an
    /// activity carrying a marker is described by two: the activity itself and the
    /// marker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty means "not something the studio offers" — a sequence flow's condition,
    /// a definitions wrapper, an extension element. Those are not gaps and must not
    /// be reported as unsupported; the manifest describes what the studio offers,
    /// not every node BPMN permits.
    /// </para>
    /// <para>
    /// Returning a set rather than a first match is load-bearing. A business rule
    /// task carrying a multi-instance marker has two descriptions, and only one of
    /// them is the one that cannot run; a first-match lookup ordered by the manifest
    /// would answer "Multi-Instance (Parallel), executes" and let it deploy.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Element> Match(XElement node)
    {
        var localName = node.Name.LocalName;
        var variant = VariantDiscriminatorFor(node);
        var markers = MarkerDiscriminatorsFor(node);

        var matches = new List<Element>();
        foreach (var element in Elements)
        {
            // Activity markers attach to any activity rather than being their own
            // element, so the manifest keys them on the marker alone.
            if (element.LocalName == "*")
            {
                if (element.EventDefinition is not null && markers.Contains(element.EventDefinition))
                {
                    matches.Add(element);
                }
                continue;
            }

            if (element.LocalName == localName && element.EventDefinition == variant)
            {
                matches.Add(element);
            }
        }

        return matches;
    }

    /// <summary>
    /// What tells one variant of an element from another: its event definition
    /// child, or the attribute that makes a subprocess an event subprocess.
    /// </summary>
    private static string? VariantDiscriminatorFor(XElement node)
    {
        // An event definition child — the eight boundary variants differ only here.
        var definition = node.Elements()
            .FirstOrDefault(child => child.Name.Namespace == Bpmn
                && child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal));
        if (definition is not null)
        {
            var name = definition.Name.LocalName;
            return name[..^"EventDefinition".Length];
        }

        // An event subprocess is a subProcess with an attribute, not its own element.
        if (node.Name.LocalName == "subProcess"
            && IsTrue(node.Attribute("triggeredByEvent")))
        {
            return "triggeredByEvent";
        }

        return null;
    }

    /// <summary>The activity markers present on a node, in manifest key form.</summary>
    private static HashSet<string> MarkerDiscriminatorsFor(XElement node)
    {
        var markers = new HashSet<string>(StringComparer.Ordinal);

        if (node.Element(Bpmn + "standardLoopCharacteristics") is not null)
        {
            markers.Add("standardLoopCharacteristics");
        }

        var multi = node.Element(Bpmn + "multiInstanceLoopCharacteristics");
        if (multi is not null)
        {
            markers.Add(IsTrue(multi.Attribute("isSequential"))
                ? "multiInstanceLoopCharacteristics:sequential"
                : "multiInstanceLoopCharacteristics:parallel");
        }

        if (IsTrue(node.Attribute("isForCompensation")))
        {
            markers.Add("isForCompensation");
        }

        return markers;
    }

    private static bool IsTrue(XAttribute? attribute) =>
        string.Equals(attribute?.Value, "true", StringComparison.OrdinalIgnoreCase);
}
