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

    /// <summary>The ids of elements nested INSIDE <paramref name="id"/> (#434).</summary>
    /// <remarks>
    /// A container's declared effect is something inside it. Descendants of the
    /// real element, so a nested same-tag child no longer truncates the window
    /// the way the old close-tag search did.
    /// </remarks>
    internal static IReadOnlyCollection<string> NestedIdsIn(string xml, string id)
    {
        return ElementIn(xml, id)
            .Descendants()
            .Select(e => (string?)e.Attribute("id"))
            .Where(found => found is not null && found != id)
            .Select(found => found!)
            .ToHashSet(StringComparer.Ordinal);
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
}
