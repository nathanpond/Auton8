using System.Xml.Linq;
using Xunit;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Pure reads over a BPMN diagram, shared by the live-engine oracle and its
/// own tests (#474).
/// </summary>
/// <remarks>
/// <para>
/// These were <c>private static</c> on <c>ExecutionEvidenceExecutionTests</c>,
/// which is <c>[Trait("RequiresService", "Flowable")]</c> — so functions that
/// need no engine at all were only exercised when somebody stood one up. Every
/// one of them was fixed for a specific misreading of XML, and each of those
/// fixes was unguarded on GitHub.
/// </para>
/// <para>
/// They live here rather than being reached through a loosened <c>private</c>,
/// because that would leave them in the traited class where GitHub never runs
/// them — the test would move, and the coverage would not.
/// </para>
/// <para>
/// What these do NOT tell you: whether the observers <em>call</em> them
/// correctly. That needs an engine and stays in the full tier.
/// </para>
/// </remarks>
internal static class BpmnDiagram
{
    /// <summary>The element carrying <paramref name="id"/>, or a failed assertion.</summary>
    internal static XElement ElementIn(string xml, string id)
    {
        var found = XDocument.Parse(xml)
            .Descendants()
            .FirstOrDefault(e => (string?)e.Attribute("id") == id);

        Assert.True(found is not null, $"No element in this diagram carries id '{id}'.");
        return found!;
    }

    /// <summary>The BPMN element this diagram gives the id `Ev_1`.</summary>
    /// <remarks>
    /// <c>Name.LocalName</c>, not the raw tag: a diagram may write
    /// <c>&lt;bpmn:startEvent&gt;</c>, <c>&lt;startEvent&gt;</c> or any other
    /// prefix for the same element. An earlier regex over the markup could not
    /// see a prefixed tag at all (#412).
    /// </remarks>
    internal static string ElementTypeIn(string xml) => ElementIn(xml, "Ev_1").Name.LocalName;

    /// <summary>
    /// The event definition `Ev_1` carries, in the manifest's vocabulary (#435).
    /// </summary>
    /// <remarks>
    /// <c>&lt;timerEventDefinition/&gt;</c> -> "timer", to match
    /// <c>bpmn-support.json</c>'s <c>eventDefinition</c> column. Null when the
    /// element carries none, which is itself the assertion for a None event.
    /// DIRECT children only: a definition belonging to something nested inside a
    /// container is not the container's.
    /// </remarks>
    internal static string? EventDefinitionIn(string xml)
    {
        const string Suffix = "EventDefinition";

        var definition = ElementIn(xml, "Ev_1").Elements()
            .Select(e => e.Name.LocalName)
            .FirstOrDefault(name => name.EndsWith(Suffix, StringComparison.Ordinal));

        return definition?[..^Suffix.Length];
    }

    /// <summary>
    /// The activity marker `Ev_1` carries, in the manifest's vocabulary (#471).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A marker is not an element. <c>bpmn-support.json</c> gives these rows
    /// <c>localName: "*"</c> — they are something a host activity carries, not a
    /// tag of their own — so the oracle's identity assertion had nothing to
    /// compare and three rows went undeclared. One of them is the Loop Marker
    /// archetype the whole milestone opens on: a multi-instance marker Flowable
    /// accepted at deployment and then ran once where the author asked for three.
    /// </para>
    /// <para>
    /// Returns the manifest's own spelling so the comparison is
    /// manifest-against-diagram, exactly as it is for a tag row — the diagram
    /// never gets to state its own expectation (#412).
    /// </para>
    /// <para>
    /// <c>isSequential</c> defaults to parallel when absent, which is what BPMN
    /// says and what Flowable does. Treating "absent" as its own answer would
    /// make a diagram that omits the attribute match neither row.
    /// </para>
    /// </remarks>
    internal static string? MarkerIn(string xml) => MarkerOf(ElementIn(xml, "Ev_1"));

    /// <summary>
    /// <see cref="MarkerIn"/> against an element already in hand — the deployed
    /// form read back from the engine, which is not a whole document.
    /// </summary>
    internal static string? MarkerOf(XElement element)
    {
        var loop = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "multiInstanceLoopCharacteristics");

        if (loop is not null)
        {
            var sequential = string.Equals(
                (string?)loop.Attribute("isSequential"), "true", StringComparison.OrdinalIgnoreCase);

            return sequential
                ? "multiInstanceLoopCharacteristics:sequential"
                : "multiInstanceLoopCharacteristics:parallel";
        }

        if (element.Elements().Any(e => e.Name.LocalName == "standardLoopCharacteristics"))
        {
            return "standardLoopCharacteristics";
        }

        return string.Equals(
            (string?)element.Attribute("isForCompensation"), "true", StringComparison.OrdinalIgnoreCase)
            ? "isForCompensation"
            : null;
    }

    /// <summary>
    /// How many instances the author asked `Ev_1`'s multi-instance marker for (#471).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads both spellings, because Auton8 rewrites between them: the stored
    /// form carries <c>autonate:loopCardinality</c> as an <b>attribute</b>, and
    /// publish turns it into a <c>&lt;bpmn:loopCardinality&gt;</c> child for the
    /// engine. A reader that knew only one would report "no cardinality" for
    /// half the lifecycle of the same diagram.
    /// </para>
    /// <para>
    /// This is the author's INTENT, and it is the half of #325 that was never
    /// checkable: "ran exactly once where the author asked for three" is a
    /// comparison between this number and what the engine did.
    /// </para>
    /// </remarks>
    internal static int? LoopCardinalityIn(string xml)
    {
        var loop = ElementIn(xml, "Ev_1").Elements()
            .FirstOrDefault(e => e.Name.LocalName == "multiInstanceLoopCharacteristics");

        if (loop is null) return null;

        var attribute = loop.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "loopCardinality")?.Value;

        var child = loop.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "loopCardinality")?.Value;

        return int.TryParse(attribute ?? child, out var count) ? count : null;
    }

    /// <summary>The ids of elements nested INSIDE <paramref name="id"/> (#434).</summary>
    /// <remarks>
    /// A container's declared effect is something inside it. Descendants of the
    /// real element, so a nested same-tag child no longer truncates the window
    /// the way the old close-tag search did.
    /// </remarks>
    internal static IReadOnlyCollection<string> NestedIdsIn(string xml, string id)
    {
        var element = ElementIn(xml, id);
        var nested = element
            .Descendants()
            .Select(e => (string?)e.Attribute("id"))
            .Where(found => found is not null && found != id)
            .Select(found => found!)
            .ToHashSet(StringComparer.Ordinal);

        // #169. A participant contains the process it REFERENCES, not one it
        // nests: `<participant processRef="p"/>` is an empty element in the XML,
        // and its process is a sibling of the collaboration. Without this the
        // oracle's task-appears check -- "the task belongs to an activity inside
        // Ev_1" -- would look at an element with no children and conclude the pool
        // created nothing, for a pool whose process created a task. The
        // containment is what a pool IS, so it is expressed here rather than by
        // making the pool's cell special-case its own effect.
        if (element.Name.LocalName == "participant"
            && (string?)element.Attribute("processRef") is { Length: > 0 } processRef)
        {
            var process = element.Document?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "process" && (string?)e.Attribute("id") == processRef);
            if (process is not null)
            {
                nested.UnionWith(process.Descendants()
                    .Select(e => (string?)e.Attribute("id"))
                    .Where(found => found is not null)
                    .Select(found => found!));
            }
        }

        // #171. A lane contains the nodes it REFERENCES: `<flowNodeRef>` carries
        // the id as text, and the node itself is a sibling of the laneSet. The
        // same shape as the participant above, for the same reason.
        if (element.Name.LocalName == "lane")
        {
            nested.UnionWith(element.Elements()
                .Where(e => e.Name.LocalName == "flowNodeRef")
                .Select(e => e.Value.Trim())
                .Where(found => found.Length > 0 && found != id));
        }

        return nested;
    }

    /// <summary>Every call activity in this diagram (#445).</summary>
    internal static IReadOnlyCollection<string> CallActivityIdsIn(string xml)
    {
        return XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "callActivity")
            .Select(e => (string?)e.Attribute("id") ?? "")
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Do this element's outgoing flows carry conditions? (#452)</summary>
    /// <remarks>
    /// The routing complement keys on this rather than on the gateway's type.
    /// Scoping it by type turned the parallel cell red for forking to both
    /// branches, which is what a parallel gateway is supposed to do.
    /// </remarks>
    internal static bool ConditionalFlowsFrom(string xml, string source) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "sequenceFlow")
            .Where(e => (string?)e.Attribute("sourceRef") == source)
            .Any(e => e.Elements().Any(c => c.Name.LocalName == "conditionExpression"));

    /// <summary>The ids this diagram's sequence flows carry away from an element.</summary>
    internal static IReadOnlyCollection<string> FlowTargetsOf(string xml, string source)
    {
        return XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "sequenceFlow")
            .Where(e => (string?)e.Attribute("sourceRef") == source)
            .Select(e => (string?)e.Attribute("targetRef"))
            .Where(target => target is not null)
            .Select(target => target!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Does this element choose its branch with a routing script (#533)?
    /// </summary>
    /// <remarks>
    /// The routing complement asks one question: did this diagram ask the element
    /// to take ONE branch? For an exclusive or inclusive gateway that is
    /// conditions on the outgoing flows (<see cref="ConditionalFlowsFrom"/>). A
    /// complex gateway's authored flows carry no conditions — publish adds them —
    /// so the author's instruction lives in the gateway's own
    /// <c>&lt;bpmn:script&gt;</c> instead. Same question, read where this element
    /// answers it.
    /// </remarks>
    internal static bool RoutesByScript(string xml, string id)
    {
        var element = XDocument.Parse(xml).Descendants()
            .FirstOrDefault(e => (string?)e.Attribute("id") == id);

        return element is not null
               && element.Name.LocalName == "complexGateway"
               && element.Elements().Any(child => child.Name.LocalName == "script");
    }

    /// <summary>
    /// An identity carried as an ATTRIBUTE rather than a child element (#532).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An event sub-process is <c>(subProcess, "triggeredByEvent")</c>, and
    /// <c>triggeredByEvent</c> is an attribute — so <see cref="EventDefinitionIn"/>,
    /// which reads a child whose name ends in <c>EventDefinition</c>, answers
    /// <c>null</c> and the identity assertion compares it against
    /// <c>"triggeredByEvent"</c>. Same shape as the marker rows, which needed
    /// their own vocabulary branch for the same reason.
    /// </para>
    /// <para>
    /// A plain <c>subProcess</c> answers <c>null</c> here, which is the point: the
    /// branch must not let a row with a MISSING identity pass. Nothing is OR-ed
    /// into the assertion — one reader is swapped for another on the rows whose
    /// manifest identity says so.
    /// </para>
    /// </remarks>
    internal static string? AttributeIdentityIn(string xml) => AttributeIdentityOf(ElementIn(xml, "Ev_1"));

    internal static string? AttributeIdentityOf(XElement element) =>
        string.Equals(
            (string?)element.Attribute("triggeredByEvent"), "true", StringComparison.OrdinalIgnoreCase)
            ? "triggeredByEvent"
            : null;

    /// <summary>
    /// What a data object reference resolves to: the declared name and value (#534).
    /// </summary>
    /// <remarks>
    /// Follows this reference's own <c>dataObjectRef</c> to the
    /// <c>&lt;bpmn:dataObject&gt;</c> it points at, and reads that declaration's
    /// <c>name</c> plus its <c>&lt;flowable:value&gt;</c>. Following a *different*
    /// reference's target would let the cell certify a value this element never
    /// pointed at, which is the resolution being tested.
    /// </remarks>
    internal static (string? Name, string? Value) DataObjectDeclarationOf(string xml, string id)
    {
        var root = XDocument.Parse(xml).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "dataObjectReference"
                                 && (string?)e.Attribute("id") == id);

        if ((string?)root?.Attribute("dataObjectRef") is not { } target) return (null, null);

        var declaration = XDocument.Parse(xml).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "dataObject"
                                 && (string?)e.Attribute("id") == target);

        if (declaration is null) return (null, null);

        var value = declaration.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "value")?.Value;

        return ((string?)declaration.Attribute("name"), value);
    }

    /// <summary>
    /// The compensation handler a boundary event is associated with (#531).
    /// </summary>
    /// <remarks>
    /// A compensation boundary's outgoing edge is an <c>&lt;association&gt;</c>,
    /// not a <c>&lt;sequenceFlow&gt;</c> — so <see cref="FlowTargetsOf"/> returns
    /// nothing for one and the `variable-written` observer would reject the
    /// handler's write as somebody else's. This is that one edge, and only that
    /// one: the association whose <c>sourceRef</c> is this element. Not "any
    /// association in the diagram", which would admit a write from anywhere an
    /// artifact happens to point.
    /// </remarks>
    internal static IReadOnlyCollection<string> CompensationHandlersOf(string xml, string source) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "association")
            .Where(e => (string?)e.Attribute("sourceRef") == source)
            .Select(e => (string?)e.Attribute("targetRef"))
            .Where(target => target is not null)
            .Select(target => target!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The activity a boundary event is attached to, or null if it is not one (#522).</summary>
    /// <remarks>
    /// Read from the DIAGRAM, because the `host-cancelled` observer has to name
    /// the host it expects to be gone, and taking that name from the engine
    /// would be deriving the expectation from the thing under test -- the defect
    /// #412 exists for. The author says which activity the boundary interrupts;
    /// the engine says whether it is still live.
    /// </remarks>
    internal static string? AttachedHostOf(string xml, string id) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "boundaryEvent")
            .Where(e => (string?)e.Attribute("id") == id)
            .Select(e => (string?)e.Attribute("attachedToRef"))
            .FirstOrDefault();
}
