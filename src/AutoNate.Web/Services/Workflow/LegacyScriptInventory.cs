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
    /// Every affected model in the store, scanned against the xml that is
    /// actually deployed (#558).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both callers used to read <see cref="IWorkflowModelStore.ListAsync"/>,
    /// which returns each row's WORKING copy, and report publication as
    /// <c>!IsDraft</c>. Two wrong answers followed from that. A workflow
    /// published with a removed-API script and since draft-edited scanned clean
    /// while its deployed definition still failed on its next run. And
    /// <c>IsDraft</c> is set by <c>NormalizeDraftState</c> on any definition
    /// change, so the row silently left the "already published, so they fail on
    /// their next run" tally the moment anybody touched the draft.
    /// </para>
    /// <para>
    /// So: published models are scanned against their published version's xml
    /// and reported as published; a model with no published version is scanned
    /// against its draft and reported as not published.
    /// </para>
    /// <para>
    /// <b>One consequence, stated rather than hidden.</b> A published model
    /// whose published xml is clean but whose draft carries a legacy script no
    /// longer appears here. That is the correct division of labour rather than a
    /// loss: #151 refuses exactly that at publish, with a message about the
    /// specific script, and this surface exists for the case #151 cannot help
    /// with — a diagram deployed before the rule existed.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<ModelFindings>> ScanStoreAsync(
        IWorkflowModelStore store, CancellationToken cancellationToken = default)
    {
        var drafts = await store.ListAsync(cancellationToken);
        var published = await store.ListPublishedAsync(cancellationToken);

        var publishedById = published.ToDictionary(model => model.Id);

        return drafts
            .Select(model =>
            {
                var isPublished = publishedById.TryGetValue(model.Id, out var publishedModel);
                var xml = isPublished ? publishedModel!.BpmnXml : model.BpmnXml;

                return new ModelFindings(
                    model.Id, model.Name, model.ProcessKey, isPublished, Scan(xml));
            })
            .Where(result => result.Findings.Count > 0)
            .OrderByDescending(result => result.IsPublished)
            .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
