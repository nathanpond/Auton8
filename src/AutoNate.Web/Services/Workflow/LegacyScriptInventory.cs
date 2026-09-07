using System.Xml.Linq;

namespace AutoNate.Web.Services.Workflow;

// Finds saved workflow models whose script tasks were written against the API
// that #147 removed (#194).
//
// #151 catches the old shape at publish, which protects everything authored
// from now on. It does nothing for diagrams that were already published: those
// fail at runtime, on whoever happens to run the process, and before this an
// operator had no way to find them except by waiting.
//
// Detection is `ScriptSurfaceRules.FindRejected` — the same list that drives
// publish-time rejection and the test panel's refusal classification. A fourth
// consumer with its own copy of the rules is how they would start disagreeing.
public static class LegacyScriptInventory
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    public sealed record Finding(string ScriptTask, string Problem);

    public sealed record ModelFindings(
        Guid WorkflowModelId,
        string Name,
        string ProcessKey,
        bool IsPublished,
        IReadOnlyList<Finding> Findings);

    /// <summary>
    /// Every rejected shape in one model's BPMN, keyed by script task.
    /// </summary>
    /// <remarks>
    /// Unparseable XML yields nothing rather than throwing: this runs over
    /// whatever is already stored, including rows saved by older builds, and a
    /// report that dies on one malformed model tells an operator less than a
    /// report that skips it.
    /// </remarks>
    public static IReadOnlyList<Finding> Scan(string? bpmnXml)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml)) return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(bpmnXml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var findings = new List<Finding>();
        foreach (var scriptTask in document.Descendants(Bpmn + "scriptTask"))
        {
            var label = scriptTask.Attribute("name")?.Value
                ?? scriptTask.Attribute("id")?.Value
                ?? "Unnamed script task";
            var body = scriptTask.Element(Bpmn + "script")?.Value;
            foreach (var problem in ScriptSurfaceRules.FindRejected(body))
            {
                findings.Add(new Finding(label, problem));
            }
        }
        return findings;
    }
}
