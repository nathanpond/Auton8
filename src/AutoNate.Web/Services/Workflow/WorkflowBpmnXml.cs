using AutoNate.Web.Models;
using System.Collections.Frozen;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

public static partial class WorkflowBpmnXml
{
    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace BpmndiNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace FlowableNamespace = "http://flowable.org/bpmn";
    private static readonly XNamespace DcNamespace = "http://www.omg.org/spec/DD/20100524/DC";

    // Default Dapr topic for signal start events when the user doesn't override
    // it on the signal in the modeler. External producers publish to this topic
    // unless a workflow opts into a custom topic per signal.
    /// <summary>
    /// <c>xsi:type</c> for a formal expression, in this document's own prefix (#482).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>xsi:type</c> takes a QName, and six places hard-coded the prefix
    /// <c>bpmn</c>. A diagram that binds the BPMN namespace as the DEFAULT --
    /// perfectly legal, and what a hand-written or API-posted diagram often does
    /// -- leaves that prefix unbound, and Flowable refuses the whole deployment
    /// with nothing actionable reaching the author. bpmn-js emits prefixed
    /// documents, so everything from the studio hid it.
    /// </para>
    /// <para>
    /// #482 reported one of the six, because that is the one #471's minimal
    /// diagrams happened to hit. Fixing only that one would have left five live,
    /// and the complex gateway's expansion was measurably among them: with the
    /// oracle's diagrams switched to the default namespace its cell failed the
    /// same way the multi-instance ones had.
    /// </para>
    /// <para>
    /// An empty prefix is the RIGHT answer, not a dropped attribute: an
    /// unprefixed QName in an attribute value resolves against the default
    /// namespace, which in that spelling is the BPMN namespace. The type
    /// declaration survives both ways round.
    /// </para>
    /// <para>
    /// <paramref name="context"/> must be an element already in the document --
    /// a freshly constructed one has no namespace scope and would always answer
    /// "unprefixed", which is wrong for exactly the documents that work today.
    /// </para>
    /// </remarks>
    private static string FormalExpressionType(XElement context)
    {
        var prefix = context.GetPrefixOfNamespace(BpmnNamespace);
        return string.IsNullOrEmpty(prefix) ? "tFormalExpression" : $"{prefix}:tFormalExpression";
    }

    public const string DefaultSignalTopic = "workflow.signals";

    /// <summary>Where a message start event listens unless its author says otherwise (#524).</summary>
    /// <remarks>
    /// Its own topic rather than sharing the signal one, so the ordinary case
    /// keeps the two streams apart without anybody configuring anything. It is
    /// not what ENFORCES the separation -- the separate registries do that, and
    /// they hold even when an author points both kinds at one topic.
    /// </remarks>
    public const string DefaultMessageTopic = "workflow.messages";
    private static readonly HashSet<string> ReplaceableTaskElementNames =
    [
        "task",
        "userTask",
        "serviceTask",
        "scriptTask",
        "businessRuleTask",
        "sendTask",
        "receiveTask",
        "manualTask"
    ];

    public static string CreateStarterDiagram(string processKey, string workflowName)
    {
        var normalizedProcessKey = NormalizeProcessKey(processKey);
        var normalizedWorkflowName = NormalizeWorkflowName(workflowName);

        return $$"""
                 <?xml version="1.0" encoding="UTF-8"?>
                 <bpmn:definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                   xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                   xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                   xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                   xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                   xmlns:flowable="http://flowable.org/bpmn"
                                   xmlns:autonate="http://autonate.dev/workflows"
                                   id="Definitions_{{normalizedProcessKey}}"
                                   targetNamespace="http://autonate.dev/workflows">
                 <bpmn:process id="{{normalizedProcessKey}}" name="{{SecurityElement.Escape(normalizedWorkflowName)}}" isExecutable="true">
                 </bpmn:process>
                 <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                   <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="{{normalizedProcessKey}}">
                   </bpmndi:BPMNPlane>
                 </bpmndi:BPMNDiagram>
               </bpmn:definitions>
               """.TrimStart();
    }

    /// <summary>
    /// The one thing publish needs from prepare (#653): the primary process
    /// carries the model's key, so the definition the engine produces is the
    /// one the record is looked up by. Nothing else -- prepare's other rewrites
    /// (async script tasks, gateway conditions, snapshots) change what a
    /// diagram MEANS, and a caller who publishes raw XML through the API is
    /// asking for that XML to run. Measured: running full prepare here made the
    /// engine refuse four compensation diagrams and turned a synchronous script
    /// failure asynchronous. A no-op when the id already is the key.
    /// </summary>
    public static string AlignPrimaryProcessKey(string xml, string processKey)
    {
        if (string.IsNullOrWhiteSpace(xml)) return xml;
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var process = ResolvePrimaryProcess(document);
        if (process is null) return xml;

        var oldId = process.Attribute("id")?.Value;
        var newId = NormalizeProcessKey(processKey);
        if (string.IsNullOrWhiteSpace(oldId) || string.Equals(oldId, newId, StringComparison.Ordinal)) return xml;

        process.SetAttributeValue("id", newId);
        foreach (var participant in document.Descendants(BpmnNamespace + "participant")
                     .Where(p => p.Attribute("processRef")?.Value == oldId))
        {
            participant.SetAttributeValue("processRef", newId);
        }
        foreach (var plane in document.Descendants(BpmndiNamespace + "BPMNPlane")
                     .Where(p => p.Attribute("bpmnElement")?.Value == oldId))
        {
            plane.SetAttributeValue("bpmnElement", newId);
        }

        var declaration = document.Declaration is null ? "" : $"{document.Declaration}\n";
        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    public static string ApplyProcessMetadata(string xml, string processKey, string workflowName)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        ApplyElementSnapshots(document, []);
        return ApplyProcessMetadata(document, processKey, workflowName);
    }

    public static string ApplyProcessMetadata(
        string xml,
        string processKey,
        string workflowName,
        IReadOnlyCollection<WorkflowElementSnapshot> elementSnapshots)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        ApplyElementSnapshots(document, elementSnapshots);
        return ApplyProcessMetadata(document, processKey, workflowName);
    }

    // Reserved variable name set by the SPA when the user clicks a button
    // in the gateway-choice modal. Synthetic conditions emitted by
    // ApplyAutoNateGatewayConditions reference this variable; treat it as
    // off-limits to authors — the system overwrites it on every choice.
    public const string GatewayChoiceVariableName = "__autonateChosenFlow";

    private static string ApplyProcessMetadata(XDocument document, string processKey, string workflowName)
    {
        // #169. THE PRIMARY PROCESS, not the first one in the file. A collaboration
        // lists its processes in whatever order the modeller wrote them, and the
        // first may be a drawn-only counterparty with nothing in it. The primary is
        // the first participant, in collaboration order, whose process contains a
        // flow node -- for a single-pool or no-pool diagram that is exactly the
        // `FirstOrDefault()` this used to be, which is the regression that matters.
        var processElement = ResolvePrimaryProcess(document)
            ?? throw new InvalidOperationException(BuildMissingProcessDefinitionMessage(document));

        EnsureFlowableNamespaceDeclared(document);
        EnsureAutoNateNamespaceDeclared(document);
        PruneOrphanSignalRoots(document);

        var oldProcessKey = processElement.Attribute("id")?.Value;
        var normalizedProcessKey = NormalizeProcessKey(processKey);
        var normalizedWorkflowName = NormalizeWorkflowName(workflowName);

        processElement.SetAttributeValue("id", normalizedProcessKey);
        processElement.SetAttributeValue("name", normalizedWorkflowName);
        processElement.SetAttributeValue("isExecutable", "true");

        // #169. Renaming the primary process used to leave its participant
        // pointing at the OLD id -- the defect the replan found at save, before
        // publish was ever reached. The other pools keep their authored ids and
        // take their participant's name as the process name, so the engine's
        // definition name IS the pool name and an execution can say which
        // participant it belongs to without a second lookup.
        ApplyCollaborationMetadata(document, processElement, oldProcessKey, normalizedProcessKey);

        foreach (var plane in document.Descendants(BpmndiNamespace + "BPMNPlane"))
        {
            var bpmnElement = plane.Attribute("bpmnElement");
            if (string.IsNullOrWhiteSpace(bpmnElement?.Value) || bpmnElement.Value == oldProcessKey)
            {
                plane.SetAttributeValue("bpmnElement", normalizedProcessKey);
            }
        }

        ForceAsyncScriptTasks(document);
        ApplyAutoNateGatewayConditions(document);

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    // Script tasks always run on Flowable's job executor so a thrown error becomes
    // a job failure (visible via job.execution.failed) instead of synchronously
    // 500ing the start-process API call.
    private static void ForceAsyncScriptTasks(XDocument document)
    {
        foreach (var scriptTask in document.Descendants(BpmnNamespace + "scriptTask"))
        {
            scriptTask.SetAttributeValue(FlowableNamespace + "async", "true");
        }
    }

    // #112. Flowable 8.0.0 executes neither message-throwing event as written:
    //
    //   * intermediateThrowEvent + messageEventDefinition is REJECTED by the
    //     deploy validator — "flowable-throw-event-invalid-eventdefinition:
    //     Unsupported intermediate throw event type".
    //   * endEvent + messageEventDefinition is worse. It deploys, ends the
    //     process cleanly, and sends nothing. A catcher on the same message name
    //     sat at one instance before and after a full run. Silent decoration that
    //     looks like it works.
    //
    // Both are rewritten at publish into a service task on the AutoNate behaviour
    // bridge — the same route a send task takes, so the throw side and the
    // receive side share one correlation model instead of growing a second.
    //
    // Only the PUBLISHED copy is rewritten. The authored diagram keeps its
    // message events, which is what lets the behaviour resolve its own message
    // name and target by activity id at run time, and what lets the studio keep
    // showing the author the shape they drew.
    /// <summary>
    /// The published copy, rewritten for an engine that cannot run what was
    /// drawn (#112). Applied at DEPLOY, never at save.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of ApplyProcessMetadata. That runs on the prepare
    /// path, and the studio saves what prepare returns — so expanding there would
    /// replace the author's message events with service tasks in their own
    /// diagram, losing the shape they drew and the configuration this expansion's
    /// behaviour reads back at run time.
    /// </remarks>
    public static string ExpandForDeployment(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return xml;

        var document = XDocument.Parse(xml);
        ExpandMessageSendEvents(document);
        ExpandSignalEndEvents(document);
        ExpandCompensationEndEvents(document);
        ExpandDataObjectTypes(document);
        // #245. Both write children of multiInstanceLoopCharacteristics, whose
        // schema sequence is extensionElements, loopCardinality, ...,
        // completionCondition. Aggregation first (it is the extensionElements),
        // then cardinality, then the condition, which appends last.
        ExpandMultiInstanceAggregation(document);
        ExpandMultiInstanceCardinality(document);
        ExpandCompletionConditions(document);
        ExpandComplexGateways(document);
        // #111. A business rule task is refused by the engine at DEPLOY, so it has
        // to become a DMN service task before the file is sent.
        ExpandBusinessRuleTasks(document);
        NamespaceScriptTaskResultVariables(document);
        ApplySignalScopes(document);
        // #645. A pool with nothing in it deploys as nothing, on EVERY path that
        // deploys. Prepare already did this; /publish deploys the body it is
        // given, and the story's own oracle cell left its empty counterparty in
        // the engine as a live definition.
        NeutraliseEmptyPools(document);
        // #171. A lane's group becomes the candidate group of the user tasks it
        // holds. Here, on the DEPLOYED copy, so the stored diagram keeps saying
        // "no assignment of its own" and the studio can say where one came from.
        ApplyLaneAssignments(document);

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Every business rule task's element id, label and decision key (#111).
    /// </summary>
    /// <remarks>
    /// The label comes back too because the publish refusal has to name the
    /// element an author can find on the canvas. An id is what the code needs and
    /// the name is what a person looks for.
    /// </remarks>
    public static IReadOnlyList<(string ElementId, string Label, string DecisionKey)>
        ExtractBusinessRuleTaskDecisions(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];

        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return []; }

        var found = new List<(string, string, string)>();
        foreach (var task in document.Descendants(BpmnNamespace + "businessRuleTask"))
        {
            var elementId = task.Attribute("id")?.Value;
            var key = task.Attribute(ScriptTaskIdentity.AutoNateNamespace + "decisionKey")?.Value;

            // A task with no key is BuildBusinessRuleTaskErrors' refusal, not
            // this one's. Reporting it twice would hand an author two sentences
            // about one element.
            if (string.IsNullOrWhiteSpace(elementId) || string.IsNullOrWhiteSpace(key)) continue;

            found.Add((elementId!, ElementLabel(task), key!.Trim()));
        }

        return found;
    }

    /// <summary>
    /// Rewrites each business rule task's decision key to the pinned key of the
    /// table version published right now (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The direct analogue of <see cref="PinCallActivityTargets"/>, for the same
    /// reason and with the same shape: Flowable resolves the reference to the
    /// LATEST version at run time — measured, by republishing a table under a
    /// running definition and watching it change its mind — and a running process
    /// must not change behaviour underneath its owner.
    /// </para>
    /// <para>
    /// Applied to the DEPLOYED copy only, BEFORE the expansion that turns the
    /// element into a DMN service task, so the pinned key is what lands in the
    /// <c>decisionTableReferenceKey</c> field. The stored diagram keeps the key
    /// the author picked, which is what the studio shows them and what the next
    /// publish resolves afresh.
    /// </para>
    /// </remarks>
    public static string PinBusinessRuleTaskDecisions(
        string xml, IReadOnlyDictionary<string, string> pinnedKeysByAuthoredKey)
    {
        ArgumentNullException.ThrowIfNull(pinnedKeysByAuthoredKey);

        if (string.IsNullOrWhiteSpace(xml) || pinnedKeysByAuthoredKey.Count == 0) return xml;

        var document = XDocument.Parse(xml);
        var changed = false;

        foreach (var task in document.Descendants(BpmnNamespace + "businessRuleTask"))
        {
            var attribute = task.Attribute(ScriptTaskIdentity.AutoNateNamespace + "decisionKey");
            var key = attribute?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!pinnedKeysByAuthoredKey.TryGetValue(key!, out var pinned)) continue;

            task.SetAttributeValue(ScriptTaskIdentity.AutoNateNamespace + "decisionKey", pinned);
            changed = true;
        }

        if (!changed) return xml;

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// A business rule task becomes a DMN service task in the deployed copy (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, not reasoned.</b> A <c>bpmn:businessRuleTask</c> does not fail
    /// at run time on this image — it fails to <b>deploy</b>, with HTTP 500:
    /// </para>
    /// <code>
    /// java.lang.NoClassDefFoundError: org/kie/api/runtime/rule/AgendaFilter
    ///   at DefaultActivityBehaviorFactory.createBusinessRuleTaskActivityBehavior(:372)
    ///   at BusinessRuleParseHandler.executeParse(BusinessRuleParseHandler.java:31)
    /// </code>
    /// <para>
    /// <c>BusinessRuleParseHandler</c> has exactly one path and resolves the
    /// behaviour at deployment time, so the KIE/Drools class is needed before any
    /// instance exists — and KIE is not in the image. <c>flowable:type="dmn"</c> is
    /// never consulted on this element. The DMN integration lives on
    /// <c>createDmnActivityBehavior(ServiceTask)</c>, which is only reachable from
    /// a service task.
    /// </para>
    /// <para>
    /// So the author draws the element BPMN means for this, and the deployed copy
    /// carries the one the engine can run. Probed end to end before this was
    /// written: the same table, the same field extension, one element name changed,
    /// deploys and writes <c>route = "big"</c> into a process variable.
    /// </para>
    /// <para>
    /// <b>Idempotent.</b> Running it over an already-expanded document finds no
    /// <c>businessRuleTask</c> and does nothing — publish runs the whole expansion
    /// chain every time, and a second pass that rewrote its own output would be a
    /// different diagram each publish.
    /// </para>
    /// </remarks>
    private static void ExpandBusinessRuleTasks(XDocument document)
    {
        foreach (var task in document.Descendants(BpmnNamespace + "businessRuleTask").ToList())
        {
            // The element NAME changes; everything else about the node is kept.
            // Rebuilding it from scratch would drop incoming/outgoing references,
            // documentation, and the multi-instance characteristics an author may
            // have set -- none of which this expansion has any business touching.
            task.Name = BpmnNamespace + "serviceTask";

            task.SetAttributeValue(FlowableNamespace + "type", "dmn");

            // ASYNC, SO A FAILED EVALUATION IS VISIBLE (#111).
            //
            // Measured on the running engine, both ways. Synchronous, a decision
            // whose expression fails throws out of the start call: HTTP 500,
            // transaction rolled back, NO instance and NO history -- the author
            // who started it gets an error page and anyone else gets nothing at
            // all. Asynchronous, the same failure becomes a retrying job carrying
            // the engine's own message ("DMN decision with key X execution failed
            // ... activity 'decide'"), which is the surface #172 built and the one
            // every other failing step already lands in.
            //
            // The same reasoning, and the same attribute, as the behaviour bridge
            // (#112). The cost is that a process start returns before the decision
            // is made; the AC's "not as a silent stall" is what buys it.
            task.SetAttributeValue(FlowableNamespace + "async", "true");

            // The authoring attribute moves into the field extension the engine
            // reads, and is then STRIPPED from the deployed copy. Leaving it
            // behind is a schema violation of the same class that refused
            // `scriptFormat` on a complexGateway (#218).
            var decisionKey = task.Attribute(ScriptTaskIdentity.AutoNateNamespace + "decisionKey")?.Value;
            task.SetAttributeValue(ScriptTaskIdentity.AutoNateNamespace + "decisionKey", null);

            if (string.IsNullOrWhiteSpace(decisionKey))
            {
                // Validation refuses this at prepare, so reaching here means the
                // diagram bypassed it. Leaving the task unconfigured would deploy
                // a step that fails on whoever runs it; leaving the element as a
                // businessRuleTask fails the deployment instead, which is the
                // louder and more diagnosable of the two.
                task.Name = BpmnNamespace + "businessRuleTask";
                task.SetAttributeValue(FlowableNamespace + "type", null);
                continue;
            }

            var extensions = task.Element(BpmnNamespace + "extensionElements");
            if (extensions is null)
            {
                extensions = new XElement(BpmnNamespace + "extensionElements");
                // FIRST child: the BPMN schema requires extensionElements before
                // every other child of an activity, and Flowable validates the
                // deployed XML against the strict schema -- a violation is a 500
                // at publish, not a degradation.
                task.AddFirst(extensions);
            }

            extensions.Add(new XElement(
                FlowableNamespace + "field",
                new XAttribute("name", "decisionTableReferenceKey"),
                new XElement(FlowableNamespace + "string", new XCData(decisionKey))));
        }
    }

    private static void ExpandMessageSendEvents(XDocument document)
    {
        foreach (var element in document.Descendants().ToList())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;

            var localName = element.Name.LocalName;

            // #112 (completed later, when a test finally exercised the element).
            // A sendTask cannot carry the behaviour bridge either: Flowable
            // refuses it outright —
            //   'flowable-sendtask-invalid-implementation': One of the attributes
            //   'type' or 'operation' is mandatory on sendTask
            // — so the criterion "a send task sends through the behaviour
            // mechanism service tasks already use" is unreachable as written. It
            // is reachable by the same route the throw events take: the author
            // draws a send task and configures it like a service task, and publish
            // turns it into one.
            var isSendTask = localName == "sendTask"
                && string.Equals(
                    element.Attribute(FlowableNamespace + "behaviorKey")?.Value,
                    SendMessageBehaviorKey, StringComparison.Ordinal);

            if (localName is not ("intermediateThrowEvent" or "endEvent") && !isSendTask) continue;

            var definition = element.Elements(BpmnNamespace + "messageEventDefinition").FirstOrDefault();
            if (definition is null && !isSendTask) continue;

            var elementId = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(elementId)) continue;

            var endsProcess = localName == "endEvent";

            // The service task keeps the ORIGINAL id, so every sequence flow and
            // every BPMNShape that references it stays valid without rewriting a
            // single one.
            definition?.Remove();
            element.Name = BpmnNamespace + "serviceTask";
            element.SetAttributeValue(FlowableNamespace + "delegateExpression", AutoNateBehaviorDelegateExpression);
            element.SetAttributeValue(FlowableNamespace + "autonateServiceKind", ServiceTaskBehaviorKind);
            element.SetAttributeValue(FlowableNamespace + "behaviorKey", SendMessageBehaviorKey);
            // Sending reaches out of the process, so it is its own transaction
            // boundary: a failure retries the send rather than redoing the work
            // in front of it. Same reasoning as #168's retry point.
            element.SetAttributeValue(FlowableNamespace + "async", "true");

            if (!endsProcess) continue;

            // A message end event has to still END. The service task took its id
            // and its incoming flows, so a terminal end event is appended after
            // it.
            var process = element.Parent;
            if (process is null) continue;

            var endId = $"{elementId}_end";
            var flowId = $"{elementId}_end_flow";
            if (process.Elements(BpmnNamespace + "endEvent")
                    .Any(e => e.Attribute("id")?.Value == endId))
            {
                // Publishing twice must not append a second one.
                continue;
            }

            process.Add(new XElement(BpmnNamespace + "endEvent", new XAttribute("id", endId)));
            process.Add(new XElement(BpmnNamespace + "sequenceFlow",
                new XAttribute("id", flowId),
                new XAttribute("sourceRef", elementId),
                new XAttribute("targetRef", endId)));

            AddShapeBeside(document, elementId, endId);
        }
    }

    // #156. A signal END event raises nothing.
    //
    // Verified against Flowable 8.0.0, and it is the message end event's problem
    // exactly (#112): it deploys, ends the process cleanly, and sends no signal —
    // a catcher waiting on the same name sat untouched. An intermediate throw of
    // that same signal fired it instantly, which is what makes this a defect in
    // the element rather than in the signal.
    //
    // So the end event is rewritten into the thing that works: an intermediate
    // throw carrying the signal, followed by a plain end event. Simpler than the
    // message case, which needed the behaviour bridge, because the signal throw is
    // natively supported.
    //
    // Applied to the DEPLOYED copy only; the authored diagram keeps the end event
    // the author drew.
    private static void ExpandSignalEndEvents(XDocument document)
    {
        foreach (var endEvent in document.Descendants(BpmnNamespace + "endEvent").ToList())
        {
            var definition = endEvent.Elements(BpmnNamespace + "signalEventDefinition").FirstOrDefault();
            if (definition is null) continue;

            var elementId = endEvent.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(elementId)) continue;

            var process = endEvent.Parent;
            if (process is null) continue;

            var terminalId = $"{elementId}_end";
            if (process.Elements(BpmnNamespace + "endEvent")
                    .Any(e => e.Attribute("id")?.Value == terminalId))
            {
                // Publishing twice must not append a second terminal event.
                continue;
            }

            // Keeps the original id, so every sequence flow and diagram shape
            // pointing at it stays valid without rewriting one.
            endEvent.Name = BpmnNamespace + "intermediateThrowEvent";

            AddFlowElement(process, new XElement(BpmnNamespace + "endEvent",
                new XAttribute("id", terminalId)));
            AddFlowElement(process, new XElement(BpmnNamespace + "sequenceFlow",
                new XAttribute("id", $"{elementId}_end_flow"),
                new XAttribute("sourceRef", elementId),
                new XAttribute("targetRef", terminalId)));

            AddShapeBeside(document, elementId, terminalId);
        }
    }

    // #166. A data object's declared type, rewritten into the form the engine
    // reads.
    //
    // Two findings that do not overlap forced this, both verified against 8.0.0
    // and bpmn-js:
    //
    //   itemSubjectRef="xsd:double"  the engine types the variable `double`, but
    //                                bpmn-js DROPS the attribute on save —
    //                                moddle resolves itemSubjectRef as a
    //                                reference and a bare QName names nothing in
    //                                the document.
    //   itemSubjectRef="ItemDouble"  bpmn-js keeps it (the reference resolves),
    //                                but the engine IGNORES the indirection —
    //                                every declared variable came back `string`.
    //
    // So no single BPMN spelling both survives the modeller and types the
    // variable. The authored diagram keeps `autonate:dataType`, which survives,
    // and the DEPLOYED copy gets the bare QName, which works — the same split
    // #112, #156, #115 and #218 already use.
    private static void ExpandDataObjectTypes(XDocument document)
    {
        // The author selects the REFERENCE on the canvas — a dataObjectReference is
        // the shape, and the dataObject behind it is invisible — so the studio
        // writes the type there. The engine reads it off the dataObject, so the
        // reference's declaration is resolved onto its target here.
        var typeByDataObjectId = document.Descendants(BpmnNamespace + "dataObjectReference")
            .Select(reference => (
                Target: Trimmed(reference.Attribute("dataObjectRef")?.Value),
                Type: Trimmed(reference.Attribute(
                    ScriptTaskIdentity.AutoNateNamespace + DataObjectTypeAttribute)?.Value)))
            .Where(pair => pair.Target is not null && pair.Type is not null)
            .GroupBy(pair => pair.Target!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Type!, StringComparer.Ordinal);

        foreach (var dataObject in document.Descendants(BpmnNamespace + "dataObject"))
        {
            var declaredType = Trimmed(
                dataObject.Attribute(ScriptTaskIdentity.AutoNateNamespace + DataObjectTypeAttribute)?.Value);

            if (declaredType is null
                && Trimmed(dataObject.Attribute("id")?.Value) is { } objectId
                && typeByDataObjectId.TryGetValue(objectId, out var fromReference))
            {
                declaredType = fromReference;
            }

            if (declaredType is null) continue;

            // An author who hand-wrote itemSubjectRef meant it; do not overwrite.
            if (dataObject.Attribute("itemSubjectRef") is null)
            {
                dataObject.SetAttributeValue("itemSubjectRef", declaredType);
                EnsureTypePrefixDeclared(document, declaredType);
            }

            // Stripped from the deployed copy: it has done its job, and the
            // engine has no use for it.
            dataObject.Attribute(ScriptTaskIdentity.AutoNateNamespace + DataObjectTypeAttribute)?.Remove();
        }
    }

    // #159/#163. A completion condition an author typed in the studio, rewritten
    // into the child element the engine reads.
    //
    // Stored as an autonate: ATTRIBUTE for the same reason a data object's type
    // is (load-bearing fact 8): a `<bpmn:completionCondition>` child is a moddle
    // property on some element types and not others, and anything the modeller
    // does not model it drops on save — silently, taking the author's condition
    // with it. The attribute survives; this puts the child back on the way out.
    //
    // Order matters. In `adHocSubProcess` the completion condition must come
    // AFTER every flow element, or the deployment is refused:
    //   cvc-complex-type.2.4.d: Invalid content was found starting with element
    //   'completionCondition'
    // which is how the first hand-written probe of that element failed.
    private static void ExpandCompletionConditions(XDocument document)
    {
        var owners = document.Descendants()
            .Where(e => e.Name.Namespace == BpmnNamespace
                        && e.Name.LocalName is "adHocSubProcess" or "multiInstanceLoopCharacteristics")
            .ToList();

        foreach (var owner in owners)
        {
            var declared = Trimmed(
                owner.Attribute(ScriptTaskIdentity.AutoNateNamespace + CompletionConditionAttribute)?.Value);
            if (declared is null) continue;

            owner.Attribute(ScriptTaskIdentity.AutoNateNamespace + CompletionConditionAttribute)?.Remove();

            // An author who hand-wrote the child meant it.
            if (owner.Element(BpmnNamespace + "completionCondition") is not null) continue;

            owner.Add(new XElement(
                BpmnNamespace + "completionCondition",
                new XAttribute(XsiNamespace + "type", FormalExpressionType(owner)),
                declared));
        }
    }

    /// <summary>Where an authored completion condition lives in the stored diagram (#159/#163).</summary>
    internal const string CompletionConditionAttribute = "completionCondition";

    /// <summary>A fixed instance count, as the author wrote it (#245).</summary>
    internal const string LoopCardinalityAttribute = "loopCardinality";

    /// <summary>
    /// THE reader of a declared cardinality, in either spelling (#356, #173).
    /// </summary>
    /// <remarks>
    /// Extracted when #173 needed the VALUE as well as its presence.
    /// <c>MultiInstanceReaderAgreementTests</c> caught the first attempt, which
    /// added a second method that knew the spellings — correctly: "one fact, one
    /// reader" is the property, and two sanctioned readers would have satisfied
    /// the allowlist while recreating exactly the disagreement #356 is. So both
    /// callers now go through here, and this is the only method in the codebase
    /// that names either spelling.
    /// </remarks>
    private static string? CardinalityText(XElement loop) =>
        Trimmed(loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + LoopCardinalityAttribute)?.Value)
        ?? loop.Elements(BpmnNamespace + "loopCardinality")
            .Select(c => Trimmed(c.Value))
            .FirstOrDefault(v => v is not null);

    internal static bool DeclaresCardinality(XElement loop) => CardinalityText(loop) is not null;

    /// <summary>
    /// The literal instance count a loop declares, in EITHER spelling (#173).
    /// </summary>
    /// <remarks>
    /// The value half of <see cref="DeclaresCardinality"/>, and deliberately
    /// beside it: #356's lesson is that two readers of this one fact disagreeing
    /// is a four-round outage, so the second reader reads through the first's
    /// neighbourhood rather than growing its own opinion about spellings.
    /// Null when the loop is collection-driven — there is no literal to read, and
    /// the instance count is then whatever the engine created.
    /// </remarks>
    internal static int? DeclaredCardinality(XElement loop) =>
        int.TryParse(CardinalityText(loop), out var parsed) && parsed > 0 ? parsed : null;

    /// <summary>
    /// Every activity carrying a multi-instance marker, by element id (#173).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The diagram is the signal, because the engine's history is not.</b>
    /// Measured on Flowable 8.0.0: a parallel multi-instance with cardinality 3
    /// reports three historic rows sharing an activityId and an activityType, and
    /// no <c>multiInstanceBody</c> row — identical in shape to a loop that ran
    /// three times. Anything keyed on repetition would collapse ordinary repeated
    /// activities into a progress row they never earned.
    /// </para>
    /// <para>
    /// Read from the STORED diagram, so it answers for a finished activity too —
    /// the engine's execution tree does model the structure, and disappears the
    /// moment the activity ends.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, (bool IsSequential, int? Cardinality, string? ElementVariable)>
        ExtractMultiInstanceActivities(string xml)
    {
        var found = new Dictionary<string, (bool, int?, string?)>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(xml)) return found;

        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return found; }

        foreach (var loop in document.Descendants(BpmnNamespace + "multiInstanceLoopCharacteristics"))
        {
            var host = loop.Parent;
            var id = host?.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var sequential = string.Equals(
                Trimmed(loop.Attribute("isSequential")?.Value),
                "true", StringComparison.OrdinalIgnoreCase);

            found[id!] = (sequential, DeclaredCardinality(loop), ElementVariableName(loop));
        }

        return found;
    }

    /// <summary>Where a loop collects each run's result, if it says (#364).</summary>
    internal static string? AggregationTarget(XElement loop) =>
        Trimmed(loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + AggregateTargetAttribute)?.Value);

    /// <summary>Which variable a loop collects, if it says (#364).</summary>
    internal static string? AggregationSource(XElement loop) =>
        Trimmed(loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + AggregateSourceAttribute)?.Value);

    /// <summary>
    /// Has this loop already been given a hand-written aggregation (#364)?
    /// </summary>
    /// <remarks>
    /// <c>ExpandMultiInstanceAggregation</c> honours a hand-written
    /// <c>&lt;flowable:variableAggregation&gt;</c> — its comment says "an author who
    /// hand-wrote the aggregation meant it" — while <c>BuildMultiInstanceErrors</c>
    /// read only the <c>autonate:aggregate*</c> attributes and had never heard of
    /// the element. So an author who wrote it was told they "did not say which
    /// variable to collect", which is #356's sentence in a second rule.
    /// </remarks>
    internal static bool DeclaresAggregationElement(XElement loop) =>
        loop.Element(BpmnNamespace + "extensionElements")
            ?.Elements(FlowableNamespace + "variableAggregation")
            .Any() == true;

    /// <summary>
    /// What a loop iterates over, in either spelling, if it says (#373).
    /// </summary>
    /// <remarks>
    /// <c>WorkflowConditionValidation</c> read only <c>flowable:collection</c>
    /// while <c>DeclaresCollection</c> also accepts <c>loopDataInputRef</c>, so a
    /// multi-instance whose collection was written the spec's way produced **no**
    /// "nothing in this process sets that variable" warning at all. Two readers,
    /// different answers — the #356 family, across files, which is why #373 widened
    /// the scan past one project.
    /// </remarks>
    internal static string? CollectionName(XElement loop) =>
        Trimmed(loop.Attribute(FlowableNamespace + "collection")?.Value)
        ?? Trimmed(loop.Element(BpmnNamespace + "loopDataInputRef")?.Value);

    /// <summary>Does this loop say what to iterate over (#356)?</summary>
    /// <remarks>
    /// <c>flowable:collection</c> is namespaced; an unprefixed <c>collection</c>
    /// attribute is rejected by the BPMN XSD outright and was a dead branch (#341).
    /// <c>loopDataInputRef</c> is the spec's own element form.
    /// </remarks>
    internal static bool DeclaresCollection(XElement loop) => CollectionName(loop) is not null;

    /// <summary>
    /// THE reader of the name each run's collection item is bound to (#173).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured:</b> Flowable writes this variable as a historic variable
    /// instance scoped to each instance's own execution — three instances over
    /// <c>["alice","bob","carol"]</c> produce three <c>reviewer</c> rows, one per
    /// execution id, and they survive the activity's completion. That is what
    /// lets an expanded row say WHICH item each instance is working on, which is
    /// the whole reason the expansion is worth opening.
    /// </para>
    /// <para>
    /// One reader, and this one arrived with a second already in the codebase:
    /// <c>WorkflowConditionValidation</c> reads the same attribute to know which
    /// names a loop assigns. <c>MultiInstanceReaderAgreementTests</c> could not
    /// see it, because <c>elementVariable</c> was missing from its spellings
    /// list — so the spelling was added there in the same change rather than
    /// quietly stepping around a guard that happened to be looking elsewhere.
    /// </para>
    /// <para>
    /// Unprefixed is not read: like <c>collection</c>, an unprefixed
    /// <c>elementVariable</c> is rejected by the BPMN XSD (#341).
    /// </para>
    /// </remarks>
    internal static string? ElementVariableName(XElement loop) =>
        Trimmed(loop.Attribute(FlowableNamespace + "elementVariable")?.Value);

    /// <summary>Where each run's result is collected, and from which variable (#245).</summary>
    internal const string AggregateTargetAttribute = "aggregateTarget";

    internal const string AggregateSourceAttribute = "aggregateSource";

    /// <summary>
    /// Turns an authored fixed instance count back into its BPMN child (#245).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same reason as the completion condition one element over: bpmn-js is
    /// vendored with no Flowable moddle extension, and a
    /// <c>&lt;bpmn:loopCardinality&gt;</c> child written by hand is dropped on the
    /// author's next save. The attribute survives; this puts the child back.
    /// </para>
    /// <para>
    /// Cardinality and a collection are alternatives, not a pair — Flowable reads
    /// the collection when both are present, so writing both would silently
    /// ignore whichever the author thought they had set. A diagram carrying both
    /// is refused at publish rather than deployed with one of them inert.
    /// </para>
    /// </remarks>
    private static void ExpandMultiInstanceCardinality(XDocument document)
    {
        foreach (var loop in document
            .Descendants(BpmnNamespace + "multiInstanceLoopCharacteristics")
            .ToList())
        {
            var declared = Trimmed(
                loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + LoopCardinalityAttribute)?.Value);
            if (declared is null) continue;

            loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + LoopCardinalityAttribute)?.Remove();

            // An author who hand-wrote the child meant it.
            if (loop.Element(BpmnNamespace + "loopCardinality") is not null) continue;

            // #482's headline case. See `FormalExpressionType` for why the
            // prefix is resolved rather than assumed.
            var cardinality = new XElement(
                BpmnNamespace + "loopCardinality",
                new XAttribute(XsiNamespace + "type", FormalExpressionType(loop)),
                declared);

            // The schema sequence puts loopCardinality after extensionElements
            // and before everything else; appending would put it after the
            // completion condition and the deployment is refused with
            // cvc-complex-type.2.4.d.
            var extensions = loop.Element(BpmnNamespace + "extensionElements");
            if (extensions is not null)
            {
                extensions.AddAfterSelf(cardinality);
            }
            else
            {
                loop.AddFirst(cardinality);
            }
        }
    }

    /// <summary>
    /// Collects each instance's output into one variable on the parent (#245).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable spells this <c>flowable:variableAggregation</c>, an extension
    /// element on the loop characteristics carrying one or more
    /// <c>flowable:variable</c> children. bpmn-js drops all of it, so the author's
    /// two fields are stored as attributes and rebuilt here.
    /// </para>
    /// <para>
    /// One source variable, not many. The panel asks "collect which variable, into
    /// what" because that is the question an author has; the multi-variable form
    /// is reachable by hand-writing the extension element, which this leaves
    /// alone.
    /// </para>
    /// </remarks>
    private static void ExpandMultiInstanceAggregation(XDocument document)
    {
        foreach (var loop in document
            .Descendants(BpmnNamespace + "multiInstanceLoopCharacteristics")
            .ToList())
        {
            var target = AggregationTarget(loop);
            var source = AggregationSource(loop);

            loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + AggregateTargetAttribute)?.Remove();
            loop.Attribute(ScriptTaskIdentity.AutoNateNamespace + AggregateSourceAttribute)?.Remove();

            // A source with nowhere to go collects nothing; validation refuses
            // that pairing rather than deploying a field the author filled in and
            // the engine ignores.
            if (target is null || source is null) continue;

            var extensions = loop.Element(BpmnNamespace + "extensionElements");
            if (extensions is null)
            {
                extensions = new XElement(BpmnNamespace + "extensionElements");
                loop.AddFirst(extensions);
            }

            // An author who hand-wrote the aggregation meant it. Through the
            // shared reader, so the validator and the expansion cannot drift --
            // they had (#364).
            if (DeclaresAggregationElement(loop)) continue;

            extensions.Add(new XElement(
                FlowableNamespace + "variableAggregation",
                new XAttribute("target", target),
                new XElement(
                    FlowableNamespace + "variable",
                    new XAttribute("source", source),
                    new XAttribute("target", source))));
        }
    }

    /// <summary>
    /// The data a process declares: its data objects, stores, inputs and outputs
    /// with their declared types (#166).
    /// </summary>
    /// <remarks>
    /// This is what makes a call activity's mapping concrete rather than
    /// free-text — a parent offers the child's declarations as targets instead of
    /// asking an author to remember them. It reads the STORED spelling
    /// (`autonate:dataType`) as well as the deployed one (`itemSubjectRef`), so it
    /// works whether the child was authored here or imported.
    /// </remarks>
    public static IReadOnlyList<WorkflowDataDeclaration> ExtractDataDeclarations(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var byName = new Dictionary<string, WorkflowDataDeclaration>(StringComparer.Ordinal);

        foreach (var element in document.Descendants())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;
            if (element.Name.LocalName is not ("dataObject" or "dataObjectReference"
                or "dataStoreReference" or "dataInput" or "dataOutput"))
            {
                continue;
            }

            // A reference carries the author's name; the object behind it carries
            // the same one. Keyed by name so the pair collapses to one entry
            // rather than offering an author the same variable twice.
            var name = Trimmed(element.Attribute("name")?.Value)
                       ?? Trimmed(element.Attribute("id")?.Value);
            if (name is null) continue;

            var declaredType =
                Trimmed(element.Attribute(ScriptTaskIdentity.AutoNateNamespace + DataObjectTypeAttribute)?.Value)
                ?? Trimmed(element.Attribute("itemSubjectRef")?.Value);

            var kind = element.Name.LocalName switch
            {
                "dataInput" => "input",
                "dataOutput" => "output",
                _ => "variable"
            };

            if (byName.TryGetValue(name, out var existing))
            {
                // Keep whichever spelling actually declared a type.
                if (existing.Type is null && declaredType is not null)
                {
                    byName[name] = existing with { Type = declaredType };
                }
                continue;
            }

            byName[name] = new WorkflowDataDeclaration(name, declaredType, kind);
        }

        return byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Where a data object's declared type lives in the stored diagram (#166).</summary>
    internal const string DataObjectTypeAttribute = "dataType";

    private static readonly XNamespace XsdNamespace = "http://www.w3.org/2001/XMLSchema";

    // itemSubjectRef holds a QName, so its prefix has to be DECLARED on the
    // deployed document or the deployment is refused outright:
    //   UndeclaredPrefix: Cannot resolve 'xsd:double' as a QName: the prefix
    //   'xsd' is not declared.
    // A studio-authored diagram carries no xmlns:xsd — nothing in the modeller
    // has any reason to add one — so writing the type without this makes every
    // diagram with a typed data object fail at publish.
    private static void EnsureTypePrefixDeclared(XDocument document, string declaredType)
    {
        if (!declaredType.StartsWith("xsd:", StringComparison.Ordinal)) return;

        var definitions = document.Root;
        if (definitions is null) return;
        if (definitions.Attribute(XNamespace.Xmlns + "xsd") is not null) return;

        definitions.SetAttributeValue(XNamespace.Xmlns + "xsd", XsdNamespace.NamespaceName);
    }

    // #115. A compensation END event ends the process and compensates NOTHING.
    //
    // Verified against Flowable 8.0.0: a process whose only compensation trigger
    // was `endEvent + compensateEventDefinition` ended cleanly with an empty
    // handler trail — no handler ran. The intermediate throw form works
    // correctly, waits for the handlers, and runs them in reverse order.
    //
    // This is the THIRD element in this milestone with that exact shape, after
    // Message End (#112) and Signal End (#156): it deploys, it looks like it
    // works, and it does nothing it exists for. The remedy is the one those two
    // established — rewrite the deployed copy into the form the engine runs,
    // and leave the authored diagram alone.
    private static void ExpandCompensationEndEvents(XDocument document)
    {
        foreach (var endEvent in document.Descendants(BpmnNamespace + "endEvent").ToList())
        {
            if (endEvent.Elements(BpmnNamespace + "compensateEventDefinition").FirstOrDefault() is null)
            {
                continue;
            }

            var elementId = endEvent.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(elementId)) continue;

            var process = endEvent.Parent;
            if (process is null) continue;

            var terminalId = $"{elementId}_end";
            if (process.Elements(BpmnNamespace + "endEvent")
                    .Any(e => e.Attribute("id")?.Value == terminalId))
            {
                // Publishing twice must not append a second terminal event.
                continue;
            }

            // Keeps the original id, so every sequence flow and diagram shape
            // pointing at it stays valid without rewriting one.
            endEvent.Name = BpmnNamespace + "intermediateThrowEvent";

            AddFlowElement(process, new XElement(BpmnNamespace + "endEvent",
                new XAttribute("id", terminalId)));
            AddFlowElement(process, new XElement(BpmnNamespace + "sequenceFlow",
                new XAttribute("id", $"{elementId}_end_flow"),
                new XAttribute("sourceRef", elementId),
                new XAttribute("targetRef", terminalId)));

            AddShapeBeside(document, elementId, terminalId);
        }
    }

    // #223. Points every behaviour service task at a specific callback URL.
    //
    // Applied to the deployed copy only, and only when an override is configured —
    // which it is not in production, so nothing is stamped and every diagram uses
    // the engine's own configured URL exactly as before.
    // #218. A complex gateway's routing decision is author script, so the deployed
    // copy gains a script task in front of the gateway and conditions on the
    // gateway's own outgoing flows.
    //
    // VERIFIED AGAINST FLOWABLE 8.0.0 BEFORE THIS WAS WRITTEN, and the result
    // contradicts both #103's inventory ("DEPLOYS BUT DOES NOTHING") and this
    // story's original premise ("silently walked past"):
    //
    //   * `complexGateway` is recorded in history as activityType
    //     **exclusiveGateway**;
    //   * it evaluates `conditionExpression` on its outgoing flows;
    //   * it honours `default`;
    //   * with two conditions both true it takes ONE flow — first match wins.
    //
    // So the engine already routes. That is why this expansion generates ONE node
    // and not two: the story's "script task plus an exclusive gateway" would add a
    // second gateway to do what the author's own gateway does. Keeping the
    // author's element also means Flowable's history names an id that exists in
    // the stored diagram.
    //
    // It also means an imported diagram containing a complex gateway does not
    // stall where someone would notice — it silently takes a branch. Refusing the
    // element at publish is the only reason that has not bitten anyone.
    //
    // Only the PUBLISHED copy is rewritten. The stored model keeps the single
    // gateway the author drew.
    /// <summary>
    /// A bare <c>resultVariable</c> on a script task becomes the namespaced one (#430).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable refuses a bare <c>resultVariable</c> on a <c>bpmn:scriptTask</c>
    /// outright — <c>cvc-complex-type.3.2.2: Attribute 'resultVariable' is not
    /// allowed to appear in element 'scriptTask'</c>. #416 fixed that inside
    /// <c>ApplyScriptTaskSnapshot</c>, which only runs for an element that HAS a
    /// snapshot: the caller does <c>if (snapshot is null) continue;</c> first. A
    /// diagram published without one — a legacy document, or
    /// <c>ApplyProcessMetadata(xml, key, name, [])</c> — carried the bare
    /// attribute through to the engine and was refused at deploy.
    /// </para>
    /// <para>
    /// It belongs here rather than there. Namespacing the attribute is a property
    /// of what the engine accepts, not of what a snapshot says, so it should not
    /// have been reachable only through the snapshot path.
    /// </para>
    /// </remarks>
    private static void NamespaceScriptTaskResultVariables(XDocument document)
    {
        foreach (var element in document.Descendants()
                     .Where(e => e.Name.LocalName == "scriptTask")
                     .ToList())
        {
            var bare = element.Attribute("resultVariable");
            if (bare is null) continue;

            var value = bare.Value;
            element.SetAttributeValue("resultVariable", null);

            if (string.IsNullOrWhiteSpace(value)) continue;
            if (element.Attribute(FlowableNamespace + "resultVariable") is not null) continue;

            element.SetAttributeValue(FlowableNamespace + "resultVariable", value);
        }
    }

    private static void ExpandComplexGateways(XDocument document)
    {
        var flowsBySource = document
            .Descendants(BpmnNamespace + "sequenceFlow")
            .GroupBy(flow => flow.Attribute("sourceRef")?.Value ?? string.Empty)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var gateway in document.Descendants(BpmnNamespace + "complexGateway").ToList())
        {
            var gatewayId = gateway.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(gatewayId)) continue;
            if (gateway.Parent is null) continue;

            // Idempotent: a re-published document already carrying its generated
            // script task is left alone rather than gaining a second one.
            var scriptTaskId = ComplexGatewayScriptTaskId(gatewayId);
            if (document.Descendants(BpmnNamespace + "scriptTask")
                .Any(t => t.Attribute("id")?.Value == scriptTaskId))
            {
                continue;
            }

            if (!flowsBySource.TryGetValue(gatewayId, out var outgoing) || outgoing.Count == 0)
            {
                // Validation refuses this at publish; expansion simply declines to
                // invent a route out of a gateway that has none.
                continue;
            }

            // The default flow, if the author set one, never gets a condition —
            // BPMN forbids it, and it is the gateway's fallback by definition.
            var defaultFlowId = Trimmed(gateway.Attribute("default")?.Value);

            var routeIds = outgoing
                .Select(flow => flow.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != defaultFlowId)
                .Select(id => id!)
                .ToList();
            if (routeIds.Count == 0) continue;

            var resultVariable = ComplexGatewayRouteVariable(gatewayId);

            // #231. How many branches arrive here decides the shape.
            //
            // A gateway with ONE incoming flow keeps #218's shape exactly: there
            // is nothing to accumulate, nothing to wait for, and no second
            // arrival to absorb. Giving every already-published split-only
            // gateway an accumulator, a wait route and an end event would change
            // the runtime shape of every one of them to buy nothing -- so the
            // accumulating shape applies only where a join can actually occur.
            // The AC asks for this to be stated either way; this is the statement.
            var inboundFlows = document.Descendants(BpmnNamespace + "sequenceFlow")
                .Where(flow => flow.Attribute("targetRef")?.Value == gatewayId)
                .ToList();

            if (inboundFlows.Count > 1)
            {
                ExpandComplexGatewayJoin(
                    document, gateway, gatewayId, inboundFlows, routeIds, resultVariable, defaultFlowId);
                ApplyComplexGatewayRouteConditions(outgoing, defaultFlowId, resultVariable);
                continue;
            }

            // Rewire every flow INTO the gateway so it lands on the script task
            // instead, then flow the script task into the gateway.
            foreach (var inbound in inboundFlows)
            {
                inbound.SetAttributeValue("targetRef", scriptTaskId);
            }

            var scriptTask = new XElement(
                BpmnNamespace + "scriptTask",
                new XAttribute("id", scriptTaskId),
                // Named so the two history rows for one gateway are tellable
                // apart: the routing script ran, then the gateway routed. Both
                // map onto the same shape in the diagram, and an operator
                // reading the history needs to know which one failed.
                new XAttribute("name", ComplexGatewayScriptTaskName(gateway.Attribute("name")?.Value)),
                new XAttribute("scriptFormat", ReadComplexGatewayScriptFormat(gateway) ?? "javascript"),
                // flowable:, NOT the bare attribute. Flowable validates the
                // deployed XML against the strict BPMN schema, which has no
                // `resultVariable` on bpmn:scriptTask — a bare one is refused
                // with "Attribute 'resultVariable' is not allowed to appear in
                // element 'bpmn:scriptTask'". Verified both spellings against
                // 8.0.0: bare is REFUSED, flowable: DEPLOYS.
                new XAttribute(FlowableNamespace + "resultVariable", resultVariable),
                // ForceAsyncScriptTasks runs on the PREPARE path, which this
                // element never passed through, so async is set here explicitly.
                new XAttribute(FlowableNamespace + "async", "true"),
                // The mapping back to the author's gateway, recoverable from the
                // deployed XML alone — that is what lets the execution view show
                // the gateway when Flowable reports the script task.
                new XAttribute(FlowableNamespace + ComplexGatewaySourceAttribute, gatewayId),
                // What the script is allowed to return. The Java behaviour reads
                // this to hand the routes to the sandbox and to enforce the
                // contract on the way back.
                new XAttribute(FlowableNamespace + ComplexGatewayRoutesAttribute, string.Join(",", routeIds)),
                new XElement(BpmnNamespace + "script",
                    Trimmed(ReadComplexGatewayScript(gateway)) ?? DefaultRouteScript(routeIds[0])));

            var runAs = ScriptTaskIdentity.ReadRunAs(gateway);
            if (!string.IsNullOrWhiteSpace(runAs))
            {
                scriptTask.SetAttributeValue(
                    ScriptTaskIdentity.AutoNateNamespace + ScriptTaskIdentity.RunAsAttribute, runAs);
            }

            // The authoring data has moved to the generated task, and Flowable
            // validates the DEPLOYED xml against the strict BPMN schema — where
            // bpmn:complexGateway has no scriptFormat and no script child.
            // Leaving them refuses the whole deployment:
            //   cvc-complex-type.3.2.2: Attribute 'scriptFormat' is not allowed
            //   to appear in element 'bpmn:complexGateway'.
            // Stripping them here is also just correct: they describe how the
            // author configured the element, which the stored model keeps and
            // the engine has no use for.
            StripComplexGatewayAuthoringAttributes(gateway);

            gateway.AddBeforeSelf(scriptTask);
            AddFlowElement(gateway.Parent, new XElement(
                BpmnNamespace + "sequenceFlow",
                new XAttribute("id", $"{scriptTaskId}__flow"),
                new XAttribute("sourceRef", scriptTaskId),
                new XAttribute("targetRef", gatewayId),
                // Tagged like the script task. Flowable records a traversed
                // sequence flow as an activity, so an untagged generated flow
                // arrives in completedActivityIds as an id the author's diagram
                // has never heard of — the same defect as the node, one edge over.
                new XAttribute(FlowableNamespace + ComplexGatewaySourceAttribute, gatewayId)));

            AddShapeBeside(document, gatewayId, scriptTaskId);

            ApplyComplexGatewayRouteConditions(outgoing, defaultFlowId, resultVariable);
        }
    }

    /// <summary>
    /// The authoring data has moved to the generated node, so it leaves the
    /// gateway (#218).
    /// </summary>
    /// <remarks>
    /// Flowable validates the DEPLOYED xml against the strict BPMN schema, where
    /// <c>bpmn:complexGateway</c> has no <c>scriptFormat</c> and no
    /// <c>&lt;script&gt;</c> child. Leaving them refuses the whole deployment:
    /// <c>cvc-complex-type.3.2.2: Attribute 'scriptFormat' is not allowed to
    /// appear in element 'bpmn:complexGateway'</c>. Stripping them is also just
    /// correct — they describe how the author configured the element, which the
    /// stored model keeps and the engine has no use for.
    /// </remarks>
    private static void StripComplexGatewayAuthoringAttributes(XElement gateway)
    {
        gateway.Attribute("scriptFormat")?.Remove();
        gateway.Attribute(ScriptTaskIdentity.AutoNateNamespace + ComplexGatewayScriptAttribute)?.Remove();
        gateway.Attribute(ScriptTaskIdentity.AutoNateNamespace + ComplexGatewayScriptFormatAttribute)?.Remove();
        gateway.Attribute(ScriptTaskIdentity.AutoNateNamespace + ScriptTaskIdentity.RunAsAttribute)?.Remove();
        gateway.Attribute(ScriptTaskIdentity.RunAsAttribute)?.Remove();
        gateway.Element(BpmnNamespace + "script")?.Remove();
    }

    /// <summary>
    /// A complex gateway with more than one incoming flow becomes an
    /// accumulating join (#231).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One generated accumulator per incoming flow</b>, each stamping its own
    /// flow id. That is what makes a branch's identity knowable at all —
    /// #219 chose it over a field extension because the expansion has to generate
    /// a node anyway, so the literal costs nothing. A single shared node, which is
    /// what #218 generates, cannot tell which branch arrived.
    /// </para>
    /// <para>
    /// Each accumulator flows into the author's own gateway, so the gateway is
    /// reached once per arriving branch and routes on the same variable as
    /// before. What is new is where a token goes when the join is <b>not</b> ready:
    /// a generated outgoing flow, conditioned on the wait route, into a generated
    /// none end event. Without it the gateway would be reached with no matching
    /// condition and the engine would refuse the instance outright.
    /// </para>
    /// <para>
    /// The wait route is also what absorbs a <b>later</b> arrival on a join that
    /// has already fired — the defect #219 measured as two live tokens down one
    /// path from one join. The engine-side behaviour owns firing once; this owns
    /// giving the absorbed token somewhere to go.
    /// </para>
    /// <para>
    /// Every generated element carries <c>autonateExpandedFrom</c>, so the
    /// execution view maps all of it back to the one gateway the author drew —
    /// the same contract #218 established, extended to N nodes rather than one.
    /// </para>
    /// </remarks>
    private static void ExpandComplexGatewayJoin(
        XDocument document,
        XElement gateway,
        string gatewayId,
        List<XElement> inboundFlows,
        List<string> routeIds,
        string resultVariable,
        string? defaultFlowId)
    {
        var scriptFormat = ReadComplexGatewayScriptFormat(gateway) ?? "javascript";
        var scriptBody = Trimmed(ReadComplexGatewayScript(gateway)) ?? DefaultRouteScript(routeIds[0]);
        var runAs = ScriptTaskIdentity.ReadRunAs(gateway);

        // The script may also answer "not yet", so the wait route joins the
        // contract it is checked against. Without this the engine's route
        // enforcement would fail the activity for the one answer a join most
        // needs to give.
        var allowedRoutes = string.Join(",", routeIds.Append(ComplexGatewayWaitRoute));

        foreach (var inbound in inboundFlows)
        {
            var flowId = Trimmed(inbound.Attribute("id")?.Value);
            if (flowId is null) continue;

            var accumulatorId = ComplexGatewayAccumulatorId(gatewayId, flowId);

            var accumulator = new XElement(
                BpmnNamespace + "scriptTask",
                new XAttribute("id", accumulatorId),
                new XAttribute("name", ComplexGatewayScriptTaskName(gateway.Attribute("name")?.Value)),
                new XAttribute("scriptFormat", scriptFormat),
                new XAttribute(FlowableNamespace + "resultVariable", resultVariable),
                new XAttribute(FlowableNamespace + "async", "true"),
                new XAttribute(FlowableNamespace + ComplexGatewaySourceAttribute, gatewayId),
                new XAttribute(FlowableNamespace + ComplexGatewayRoutesAttribute, allowedRoutes),
                // The literal that gives this branch its identity.
                new XAttribute(FlowableNamespace + ComplexGatewayArrivingFlowAttribute, flowId),
                new XElement(BpmnNamespace + "script", scriptBody));

            if (!string.IsNullOrWhiteSpace(runAs))
            {
                accumulator.SetAttributeValue(
                    ScriptTaskIdentity.AutoNateNamespace + ScriptTaskIdentity.RunAsAttribute, runAs);
            }

            inbound.SetAttributeValue("targetRef", accumulatorId);

            gateway.AddBeforeSelf(accumulator);
            AddFlowElement(gateway.Parent, new XElement(
                BpmnNamespace + "sequenceFlow",
                new XAttribute("id", $"{accumulatorId}__flow"),
                new XAttribute("sourceRef", accumulatorId),
                new XAttribute("targetRef", gatewayId),
                new XAttribute(FlowableNamespace + ComplexGatewaySourceAttribute, gatewayId)));

            AddShapeBeside(document, gatewayId, accumulatorId);
        }

        StripComplexGatewayAuthoringAttributes(gateway);

        // Where a token goes when the join is not ready, and where a later
        // arrival on a fired join is absorbed.
        var waitEndId = ComplexGatewayWaitEndId(gatewayId);
        var waitEnd = new XElement(
            BpmnNamespace + "endEvent",
            new XAttribute("id", waitEndId),
            new XAttribute("name", "Waiting for more branches"),
            new XAttribute(FlowableNamespace + ComplexGatewaySourceAttribute, gatewayId));

        gateway.AddAfterSelf(waitEnd);
        AddShapeBeside(document, gatewayId, waitEndId);

        var waitFlow = new XElement(
            BpmnNamespace + "sequenceFlow",
            new XAttribute("id", $"{waitEndId}__flow"),
            new XAttribute("sourceRef", gatewayId),
            new XAttribute("targetRef", waitEndId),
            new XAttribute(FlowableNamespace + ComplexGatewaySourceAttribute, gatewayId));

        // ATTACHED FIRST, and that ordering is load-bearing. `FormalExpressionType`
        // resolves the bpmn: prefix by walking the element's ancestors, so on an
        // element that is not in the document yet it finds none and emits a bare
        // `tFormalExpression`. Flowable then refuses the whole deployment with
        //   cvc-elt.4.2: Cannot resolve 'tFormalExpression' to a type definition
        // -- measured, as a 400 on every join publish. The condition has to be
        // added to a flow that already knows where it lives.
        AddFlowElement(gateway.Parent, waitFlow);

        // A CONDITION even when the author set a default flow. A default would
        // swallow the wait token only when nothing else matched, which is also
        // when the author's own default is meant to run -- two different meanings
        // on one edge.
        waitFlow.Add(new XElement(
            BpmnNamespace + "conditionExpression",
            new XAttribute(XsiNamespace + "type", FormalExpressionType(waitFlow)),
            $"${{{resultVariable} == '{ComplexGatewayWaitRoute}'}}"));

        _ = defaultFlowId;
    }

    /// <summary>
    /// Conditions on the author's own outgoing flows (#218).
    /// </summary>
    /// <remarks>
    /// An author-written condition is left alone, exactly as
    /// <c>ApplyAutoNateGatewayConditions</c> does — the script chooses among the
    /// routes it was given, and an author who has already written a condition
    /// meant it. Extracted in #231 so the split shape and the joining shape share
    /// ONE reader of this rule rather than growing two that can disagree.
    /// </remarks>
    private static void ApplyComplexGatewayRouteConditions(
        List<XElement> outgoing, string? defaultFlowId, string resultVariable)
    {
        foreach (var flow in outgoing)
        {
            var flowId = flow.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(flowId) || flowId == defaultFlowId) continue;
            if (flow.Element(BpmnNamespace + "conditionExpression") is not null) continue;

            flow.Add(new XElement(
                BpmnNamespace + "conditionExpression",
                new XAttribute(XsiNamespace + "type", FormalExpressionType(flow)),
                $"${{{resultVariable} == '{flowId}'}}"));
        }
    }

    /// <summary>
    /// Generated element id -> the author's element it came from, read from a
    /// DEPLOYED document (#218).
    /// </summary>
    /// <remarks>
    /// Deliberately keyed off the attribute rather than the id's shape. A naming
    /// convention is not a contract, and an id-suffix rule would silently map any
    /// author element unlucky enough to end in the same characters.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> BuildExpansionSourceMap(string? deployedXml)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(deployedXml)) return map;

        XDocument document;
        try
        {
            document = XDocument.Parse(deployedXml);
        }
        catch (System.Xml.XmlException)
        {
            return map;
        }

        foreach (var element in document.Descendants())
        {
            var source = Trimmed(element.Attribute(FlowableNamespace + ComplexGatewaySourceAttribute)?.Value);
            var id = Trimmed(element.Attribute("id")?.Value);
            if (source is null || id is null) continue;
            map[id] = source;
        }

        return map;
    }

    /// <summary>The generated routing task's name for a complex gateway (#218).</summary>
    internal static string ComplexGatewayScriptTaskName(string? gatewayName)
    {
        var name = Trimmed(gatewayName);
        return name is null ? "Routing script" : $"{name} (routing script)";
    }

    /// <summary>The generated script task's id for a complex gateway (#218).</summary>
    internal static string ComplexGatewayScriptTaskId(string gatewayId) =>
        $"{gatewayId}__autonateRoute";

    /// <summary>The variable the routing script's chosen route id lands in (#218).</summary>
    internal static string ComplexGatewayRouteVariable(string gatewayId) =>
        $"__autonateRoute_{gatewayId}";

    /// <summary>
    /// The accumulator generated for one incoming flow of a joining gateway (#231).
    /// </summary>
    /// <remarks>
    /// One node per incoming flow is what makes a branch's identity knowable at
    /// all: the flow id is stamped on the node as a literal, which #219 chose over
    /// a field extension because the expansion has to generate the node anyway.
    /// </remarks>
    internal static string ComplexGatewayAccumulatorId(string gatewayId, string flowId) =>
        $"{gatewayId}__autonateAcc__{flowId}";

    /// <summary>Where a token goes when the join is not ready to fire (#231).</summary>
    internal static string ComplexGatewayWaitEndId(string gatewayId) =>
        $"{gatewayId}__autonateWaiting";

    /// <summary>The routing script's answer for "not enough has arrived yet" (#231).</summary>
    internal const string ComplexGatewayWaitRoute = "autonateWait";

    /// <summary>The incoming flow one accumulator stands for (#231).</summary>
    internal const string ComplexGatewayArrivingFlowAttribute = "autonateArrivingFlow";

    // Marks a generated node as belonging to an author's element, so the
    // execution view can map Flowable's activity ids back onto the diagram the
    // author actually drew.
    internal const string ComplexGatewaySourceAttribute = "autonateExpandedFrom";

    // The routes the script may return, as a comma-separated list of flow ids.
    internal const string ComplexGatewayRoutesAttribute = "autonateAllowedRoutes";

    // The routing script lives in an autonate: ATTRIBUTE, not a <bpmn:script>
    // child, and that is a browser fact rather than a preference.
    //
    // bpmn-js is vendored with no Flowable moddle extension, and its moddle has
    // no script property on ComplexGateway — so it DROPS a <bpmn:script> child
    // when it re-serialises the diagram. Proven, not assumed: seeding one and
    // saving in the studio came back with the script gone
    // (ComplexGatewayStudioRoundTripTests). An author would have lost their code
    // on their next save, with nothing to say so.
    //
    // Attributes in the autonate namespace survive through $attrs, which is the
    // mechanism runAs already uses, and the serialiser escapes the newlines.
    internal const string ComplexGatewayScriptAttribute = "routeScript";
    internal const string ComplexGatewayScriptFormatAttribute = "scriptFormat";

    /// <summary>An author's routing script, stored on the gateway itself.</summary>
    /// <remarks>
    /// The child element is still read, so a hand-authored or imported diagram
    /// written the obvious way works. Only the studio's own round trip needs the
    /// attribute.
    /// </remarks>
    private static string? ReadComplexGatewayScript(XElement gateway) =>
        Trimmed(gateway.Attribute(ScriptTaskIdentity.AutoNateNamespace + ComplexGatewayScriptAttribute)?.Value)
        ?? gateway.Element(BpmnNamespace + "script")?.Value;

    private static string? ReadComplexGatewayScriptFormat(XElement gateway) =>
        Trimmed(gateway.Attribute(ScriptTaskIdentity.AutoNateNamespace + ComplexGatewayScriptFormatAttribute)?.Value)
        ?? Trimmed(gateway.Attribute("scriptFormat")?.Value);

    // A freshly dropped gateway has no script yet, and publishing must not fail
    // on that — it takes the first route, which is visible and wrong rather than
    // invisible and wrong.
    private static string DefaultRouteScript(string firstRouteId) =>
        $"// Return the id of the route to take.\nreturn '{firstRouteId}';";

    public static string StampCallbackBaseUrl(string xml, string? callbackBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(callbackBaseUrl))
        {
            return xml;
        }

        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return xml; }

        var stamped = 0;
        foreach (var task in document.Descendants(BpmnNamespace + "serviceTask"))
        {
            // Only tasks on the behaviour bridge — a service task wired to
            // something else has no callback to redirect.
            if (task.Attribute(FlowableNamespace + "delegateExpression")?.Value
                != AutoNateBehaviorDelegateExpression)
            {
                continue;
            }

            task.SetAttributeValue(FlowableNamespace + "autonateCallbackBaseUrl", callbackBaseUrl);
            stamped++;
        }

        // #218. Script tasks call back too, and had the same defect one element
        // over: the engine sent every script to the container's app regardless of
        // which app published the workflow. #223 fixed it for the behaviour
        // bridge only, so a routing script — or any script task — reached the
        // wrong host in E2E.
        foreach (var task in document.Descendants(BpmnNamespace + "scriptTask"))
        {
            task.SetAttributeValue(FlowableNamespace + "autonateCallbackBaseUrl", callbackBaseUrl);
            stamped++;
        }

        if (stamped == 0) return xml;

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    // #156. The author's signal scope, moved onto the signal the engine reads.
    //
    // Scope is recorded on the EVENT in the authored diagram
    // (flowable:autonateSignalScope) because moddle refuses to attach an
    // attribute to a freshly created root element in the studio. Here it becomes
    // Flowable's own flowable:scope on the <bpmn:signal>, so the ENGINE enforces
    // the scope rather than Auton8 filtering a broadcast afterwards.
    //
    // Instance scope is the owner's decision (option 1): `instance` and `global`,
    // no "same definition" scope — Flowable has no such thing natively, and
    // building one would have meant intercepting a throw that fires inside the
    // engine.
    //
    // Two events sharing a name but not a scope are genuinely different
    // subscriptions, so a second signal element is created for the minority
    // scope rather than one of them silently winning.
    // The studio records the scope as an extension ELEMENT on the event, because
    // moddle would not let it write an attribute onto an event parsed without one.
    /// <summary>
    /// Is this event one that FORCES its signal to be global? (#274)
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a start event at PROCESS level. It exists to be triggered from
    /// outside any instance, so its signal cannot be instance-scoped.
    /// </para>
    /// <para>
    /// A start event inside <c>&lt;subProcess triggeredByEvent="true"&gt;</c> is not
    /// one of these. It is an in-instance handler — an event subprocess fires
    /// within the run that raised the signal — so it may share an instance-scoped
    /// signal quite happily. #270 classified every <c>startEvent</c> as global and
    /// refused that shape; measured against 8.0.0, it deploys (201) and runs, both
    /// the guarded task and the handler's task appearing.
    /// </para>
    /// </remarks>
    private static bool ForcesGlobalSignal(XElement element) =>
        element.Name.LocalName == "startEvent"
        && !element.Ancestors(BpmnNamespace + "subProcess")
            .Any(sub => string.Equals(
                sub.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What one event declares about its signal's scope (#278).
    /// </summary>
    /// <remarks>
    /// Four states, not two. The two that keep being conflated are
    /// <see cref="Unspecified"/> — the author said nothing, so whatever the
    /// diagram already carries stands — and <see cref="Global"/>, an explicit
    /// request to widen. Treating the first as the second strips a scope its
    /// author deliberately narrowed.
    /// </remarks>
    private enum SignalScopeDeclaration
    {
        /// <summary>Says nothing. NOT the same as saying "global".</summary>
        Unspecified,

        /// <summary>Only this process instance hears it.</summary>
        Instance,

        /// <summary>Every instance on the engine hears it.</summary>
        Global,

        /// <summary>
        /// Says something no spelling recognises — a typo. Refused at publish
        /// rather than silently read as "global", which is what shipped: a
        /// mistyped <c>instnace</c> published clean and ran engine-wide (#278).
        /// </summary>
        Unrecognised
    }

    /// <summary>
    /// The ONE place a signal-scope declaration is interpreted (#278).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This function exists because of how #156 failed verification five rounds
    /// running. Every failure was the same family: <c>ApplySignalScopes</c> and
    /// <c>BuildSignalScopeErrors</c> each read the author's declaration with
    /// their own private rules, and the two sets of rules drifted apart. Round 3
    /// they disagreed about what "declares nothing" means. Round 5 they
    /// disagreed about how "instance" is spelt — the expansion tested
    /// <c>declared == "instance"</c> while the validator accepted "instance" OR
    /// "processInstance", so a diagram saying <c>processInstance</c> passed
    /// validation and had its scope silently dropped:
    /// </para>
    /// <code>
    /// value='instance'         errors=0  emittedScope=processInstance
    /// value='processInstance'  errors=0  emittedScope=(NONE)      &lt;- leaked global
    /// value='instnace' (typo)  errors=0  emittedScope=(NONE)      &lt;- leaked global
    /// </code>
    /// <para>
    /// Five point fixes did not converge, because each one repaired a
    /// disagreement without removing the ability to disagree. The expansion and
    /// the validator both consume this function's answer and nothing else.
    ///
    /// <b>Two readers remain outside it</b>, and saying so here is the point --
    /// an earlier version of this comment claimed "a new spelling is added in one
    /// place or in none", which was false and is why nobody went looking for the
    /// others (#311). They are <c>interpretSignalScope</c> in
    /// <c>src/AutoNate.Spa/src/lib/bpmn/workflow.js</c>, which cannot call into
    /// this assembly and mirrors its states deliberately, and
    /// <c>FlowableClient.IsSignalGlobalAsync</c>, which reads the DEPLOYED
    /// diagram rather than an authored one. A new spelling goes in all three.
    /// <c>SignalScopeCasesTests.The_two_paths_never_disagree</c> is the guard.
    /// </para>
    /// </remarks>
    private static SignalScopeDeclaration ReadSignalScopeDeclaration(XElement element) =>
        // #274. A PROCESS-LEVEL start event is global by nature: it exists to be
        // triggered from outside any instance, so it cannot be scoped, and it
        // says so whether or not the author wrote anything. A start event inside
        // an event subprocess is an in-instance handler and is not one of these.
        ForcesGlobalSignal(element)
            ? SignalScopeDeclaration.Global
            : InterpretSignalScope(RawSignalScope(element));

    /// <summary>The studio's raw declaration, uninterpreted.</summary>
    /// <remarks>
    /// Recorded as an extension ELEMENT on the event, because moddle would not
    /// let the studio write an attribute onto an event parsed without one.
    /// </remarks>
    private static string? RawSignalScope(XElement element) =>
        element.Element(BpmnNamespace + "extensionElements")?
            .Elements()
            .FirstOrDefault(child => child.Name.LocalName == "autonateSignalScope")?
            .Attribute("value")?.Value;

    /// <summary>Every spelling the product accepts, in one switch.</summary>
    private static SignalScopeDeclaration InterpretSignalScope(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? SignalScopeDeclaration.Unspecified
            : raw.Trim().ToLowerInvariant() switch
            {
                // "processInstance" is Flowable's own spelling, and a diagram
                // round-tripped through another modeller carries it. "instance"
                // is the studio's. Both mean the same thing and both must, or
                // one of them leaks global.
                "instance" or "processinstance" => SignalScopeDeclaration.Instance,
                "global" => SignalScopeDeclaration.Global,
                _ => SignalScopeDeclaration.Unrecognised
            };

    /// <summary>Everything one signal NAME has been asked to be.</summary>
    /// <remarks>
    /// Keyed by name rather than by signal id because Flowable's namespace is
    /// keyed by name: two <c>&lt;bpmn:signal&gt;</c> roots sharing a name are
    /// refused at deployment whatever their ids, so scope is a property of the
    /// name and one name means one scope.
    /// </remarks>
    private sealed class SignalScopeUse
    {
        /// <summary>The root carrying this name, where exactly one does.</summary>
        public XElement? Root { get; set; }

        /// <summary>Every root carrying this name — more than one is refused.</summary>
        public List<XElement> Roots { get; } = [];

        /// <summary>Who asked for what. More than one key is a contradiction.</summary>
        public Dictionary<SignalScopeDeclaration, List<string>> DeclaredBy { get; } = new();

        /// <summary>Events whose declaration is not a spelling we know.</summary>
        public List<(string Label, string Raw)> Unrecognised { get; } = [];

        /// <summary>
        /// The single scope every declarer agreed on, or null where they did not
        /// agree, nobody declared, or somebody misspelt it. Null means "emit
        /// nothing" — the authored diagram stands and validation has the say.
        /// </summary>
        public SignalScopeDeclaration? Agreed =>
            Unrecognised.Count == 0 && DeclaredBy.Count == 1
                ? DeclaredBy.Keys.Single()
                : null;
    }

    /// <summary>
    /// Reads every signal-scope declaration in the diagram, once (#278).
    /// </summary>
    /// <remarks>
    /// The expansion and the validator both start here, so they cannot disagree
    /// about what the diagram says — only about what to do about it.
    /// </remarks>
    private static Dictionary<string, SignalScopeUse> CollectSignalScopeUses(XDocument document)
    {
        var uses = new Dictionary<string, SignalScopeUse>(StringComparer.Ordinal);

        SignalScopeUse For(string name)
        {
            if (!uses.TryGetValue(name, out var use))
            {
                use = new SignalScopeUse();
                uses[name] = use;
            }

            return use;
        }

        // The roots first, so a name that no event references is still seen — a
        // duplicate pair of them is refused whether or not anything catches it.
        foreach (var root in document.Descendants(BpmnNamespace + "signal"))
        {
            var id = Trimmed(root.Attribute("id")?.Value);
            var rootName = Trimmed(root.Attribute("name")?.Value) ?? id;
            if (rootName is null) continue;

            var use = For(rootName);
            use.Roots.Add(root);
            use.Root ??= root;
        }

        // A scope the diagram ALREADY carries is the signal's own state, not a
        // declaration by any event that happens to reference it. #279: attributing
        // it to every referencing event made "declares nothing" read as a
        // declaration, so an unscoped throw beside a pre-scoped root looked like a
        // contradiction and the whole diagram was refused. It is recorded once,
        // against the root, so it can still contradict an explicit `global`.
        foreach (var (name, use) in uses)
        {
            if (use.Roots.Count != 1 || use.Root is null) continue;

            var rawCarried = use.Root.Attribute(FlowableNamespace + "scope")?.Value;
            switch (InterpretSignalScope(rawCarried))
            {
                case SignalScopeDeclaration.Instance:
                    Declare(use, SignalScopeDeclaration.Instance, $"the signal '{name}' itself");
                    break;
                case SignalScopeDeclaration.Global:
                    Declare(use, SignalScopeDeclaration.Global, $"the signal '{name}' itself");
                    break;

                // #291. This case used to fall out of an `if` and be forgotten, so
                // a typo on the ROOT published clean and Flowable answered
                // HTTP 500 flowable-signal-invalid-scope ("Only values 'global'
                // and 'processInstance' are supported") — while the identical
                // string on an EVENT was refused with a helpful message.
                //
                // The root is not an exotic place for it: publish writes
                // flowable:scope onto the root, so a published-then-reopened
                // diagram carries it there and nowhere else (see #281).
                case SignalScopeDeclaration.Unrecognised:
                    use.Unrecognised.Add(($"the signal '{name}' itself", rawCarried!.Trim()));
                    break;

                case SignalScopeDeclaration.Unspecified:
                    // The ordinary case: the root carries no scope of its own.
                    break;
            }
        }

        foreach (var element in document.Descendants()
            .Where(e => e.Name.Namespace == BpmnNamespace))
        {
            var definition = element.Elements(BpmnNamespace + "signalEventDefinition").FirstOrDefault();
            if (definition is null) continue;

            var signalRef = Trimmed(definition.Attribute("signalRef")?.Value);
            if (signalRef is null) continue;

            var root = document.Descendants(BpmnNamespace + "signal")
                .FirstOrDefault(s => s.Attribute("id")?.Value == signalRef);
            var name = Trimmed(root?.Attribute("name")?.Value) ?? signalRef;

            var use = For(name);
            var label = LabelOf(element);

            switch (ReadSignalScopeDeclaration(element))
            {
                case SignalScopeDeclaration.Instance:
                    Declare(use, SignalScopeDeclaration.Instance, label);
                    break;
                case SignalScopeDeclaration.Global:
                    Declare(use, SignalScopeDeclaration.Global, label);
                    break;
                case SignalScopeDeclaration.Unrecognised:
                    use.Unrecognised.Add((label, RawSignalScope(element)!.Trim()));
                    break;
                // Unspecified declares nothing, and is not a conflict with
                // anything. An unscoped THROW beside an instance-scoped catch is
                // the ordinary shape: the throw raises the signal, the scope
                // decides who hears it (#273).
            }
        }

        return uses;

        static void Declare(SignalScopeUse use, SignalScopeDeclaration scope, string label)
        {
            if (!use.DeclaredBy.TryGetValue(scope, out var declarers))
            {
                declarers = [];
                use.DeclaredBy[scope] = declarers;
            }

            declarers.Add(label);
        }
    }


    /// <summary>
    /// Writes the agreed scope onto each signal root (#156, #270, #278).
    /// </summary>
    /// <remarks>
    /// <para>
    /// All of the interpretation lives in <see cref="CollectSignalScopeUses"/>.
    /// What is left here is the one decision this function actually owns: what to
    /// WRITE. A name whose declarers disagree, or that carries a misspelt
    /// declaration, is refused by <see cref="BuildSignalScopeErrors"/> — so
    /// nothing is emitted for it rather than XML the engine rejects with a 500.
    /// </para>
    /// <para>
    /// A name nobody declared is left exactly as authored. That is the case that
    /// keeps being got wrong: a diagram may already carry Flowable's own
    /// <c>flowable:scope</c>, written by hand or by another modeller, and
    /// treating "declared nothing" as "wants global" strips it — silently
    /// widening a signal its author had deliberately narrowed.
    /// </para>
    /// </remarks>
    private static void ApplySignalScopes(XDocument document)
    {
        foreach (var use in CollectSignalScopeUses(document).Values)
        {
            if (use.Root is null || use.Roots.Count != 1) continue;

            // Null means: emit nothing. Contradiction, typo, or nobody asked.
            if (use.Agreed is not { } agreed) continue;

            use.Root.SetAttributeValue(
                FlowableNamespace + "scope",
                agreed == SignalScopeDeclaration.Instance ? "processInstance" : null);
        }
    }


    // The execution diagram renders from the DEPLOYED definition, so an element
    // with no BPMNShape would be invisible there. Cloned from the element it
    // follows and nudged along, which is close enough to be legible and cannot
    // fail on a diagram that never had DI in the first place.
    // #115. Artifacts — associations, text annotations, groups — must come AFTER
    // every flow element in the strict BPMN schema, and Flowable validates the
    // deployed XML against it.
    //
    // Appending a generated node with `process.Add` therefore puts it in the
    // wrong place the moment a diagram has an association, and the whole
    // deployment is refused:
    //   cvc-complex-type.2.4.a: Invalid content was found starting with element
    //   'endEvent'. One of '{artifact, resourceRole, ...}' is expected.
    //
    // Compensation is the first expansion to meet this, because the association
    // IS how a boundary event reaches its handler — but every expansion appends,
    // so all of them route through here.
    private static void AddFlowElement(XElement process, XElement flowElement)
    {
        var firstArtifact = process.Elements()
            .FirstOrDefault(e => e.Name.Namespace == BpmnNamespace
                                 && e.Name.LocalName is "association" or "textAnnotation" or "group");

        if (firstArtifact is null)
        {
            process.Add(flowElement);
            return;
        }

        firstArtifact.AddBeforeSelf(flowElement);
    }

    private static void AddShapeBeside(XDocument document, string existingElementId, string newElementId)
    {
        var source = document.Descendants(BpmndiNamespace + "BPMNShape")
            .FirstOrDefault(shape => shape.Attribute("bpmnElement")?.Value == existingElementId);
        if (source?.Parent is null) return;

        var clone = new XElement(source);
        clone.SetAttributeValue("id", $"Shape_{newElementId}");
        clone.SetAttributeValue("bpmnElement", newElementId);

        var bounds = clone.Element(DcNamespace + "Bounds");
        if (bounds is not null)
        {
            if (double.TryParse(bounds.Attribute("x")?.Value, out var x))
            {
                bounds.SetAttributeValue("x", (x + 160).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            bounds.SetAttributeValue("width", "36");
            bounds.SetAttributeValue("height", "36");
        }

        source.Parent.Add(clone);
    }

    // For every default-mode user task that flows directly into an exclusive
    // gateway, inject a synthetic ${__autonateChosenFlow == 'FlowId'} condition
    // on each unconditioned outgoing flow. The SPA's gateway-choice modal sets
    // that variable when a user clicks a path button. Author-written conditions
    // are preserved untouched. Idempotent: re-running publish doesn't duplicate.
    private static void ApplyAutoNateGatewayConditions(XDocument document)
    {
        var flowsBySource = document
            .Descendants(BpmnNamespace + "sequenceFlow")
            .GroupBy(flow => flow.Attribute("sourceRef")?.Value ?? string.Empty)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var userTask in document.Descendants(BpmnNamespace + "userTask"))
        {
            if (!IsDefaultBehaviorUserTask(userTask))
            {
                continue;
            }

            var taskId = userTask.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(taskId)
                || !flowsBySource.TryGetValue(taskId, out var taskOutflows)
                || taskOutflows.Count != 1)
            {
                continue;
            }

            var targetRef = taskOutflows[0].Attribute("targetRef")?.Value;
            if (string.IsNullOrEmpty(targetRef))
            {
                continue;
            }

            var target = document
                .Descendants()
                .FirstOrDefault(e =>
                    e.Name.Namespace == BpmnNamespace &&
                    e.Attribute("id")?.Value == targetRef);
            if (target is null || target.Name.LocalName != "exclusiveGateway")
            {
                continue;
            }

            if (!flowsBySource.TryGetValue(targetRef, out var gatewayOutflows))
            {
                continue;
            }

            foreach (var gatewayFlow in gatewayOutflows)
            {
                if (gatewayFlow.Element(BpmnNamespace + "conditionExpression") is not null)
                {
                    continue;
                }

                var flowId = gatewayFlow.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(flowId))
                {
                    continue;
                }

                var conditionElement = new XElement(
                    BpmnNamespace + "conditionExpression",
                    new XAttribute(XsiNamespace + "type", FormalExpressionType(gatewayFlow)),
                    $"${{{GatewayChoiceVariableName} == '{flowId}'}}");
                gatewayFlow.Add(conditionElement);
            }
        }
    }

    private static bool IsDefaultBehaviorUserTask(XElement userTask)
    {
        var rawMode = userTask.Attribute(FlowableNamespace + "userFormMode")?.Value;
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            return true;
        }

        return string.Equals(rawMode.Trim(), "simple", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record GatewayChoice(string FlowId, string Label, string? Description);

    public sealed record UserTaskGatewayDescription(
        string? Description,
        IReadOnlyList<GatewayChoice> Choices);

    // Pulls (description, gateway-button choices) for one user task from the
    // published BPMN. Returns null choices when the task isn't a default-mode
    // task that flows into an exclusive gateway. Description comes from the
    // standard <bpmn:documentation> child element.
    public static UserTaskGatewayDescription? TryDescribeGatewayChoices(string? bpmnXml, string? userTaskId)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml) || string.IsNullOrWhiteSpace(userTaskId))
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(bpmnXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var userTask = document
            .Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "userTask" &&
                (string?)e.Attribute("id") == userTaskId);
        if (userTask is null)
        {
            return null;
        }

        var description = ReadDocumentationText(userTask);

        if (!IsDefaultBehaviorUserTask(userTask))
        {
            return new UserTaskGatewayDescription(description, Array.Empty<GatewayChoice>());
        }

        var taskOutflows = document
            .Descendants(BpmnNamespace + "sequenceFlow")
            .Where(flow => flow.Attribute("sourceRef")?.Value == userTaskId)
            .ToArray();
        if (taskOutflows.Length != 1)
        {
            return new UserTaskGatewayDescription(description, Array.Empty<GatewayChoice>());
        }

        var targetRef = taskOutflows[0].Attribute("targetRef")?.Value;
        if (string.IsNullOrEmpty(targetRef))
        {
            return new UserTaskGatewayDescription(description, Array.Empty<GatewayChoice>());
        }

        var target = document
            .Descendants(BpmnNamespace + "exclusiveGateway")
            .FirstOrDefault(e => e.Attribute("id")?.Value == targetRef);
        if (target is null)
        {
            return new UserTaskGatewayDescription(description, Array.Empty<GatewayChoice>());
        }

        var choices = document
            .Descendants(BpmnNamespace + "sequenceFlow")
            .Where(flow => flow.Attribute("sourceRef")?.Value == targetRef)
            .Select(flow =>
            {
                var flowId = flow.Attribute("id")?.Value ?? string.Empty;
                var label = flow.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = flowId;
                }
                var flowDescription = ReadDocumentationText(flow);
                return new GatewayChoice(flowId, label!, flowDescription);
            })
            .Where(choice => !string.IsNullOrEmpty(choice.FlowId))
            .ToArray();

        return new UserTaskGatewayDescription(description, choices);
    }

    private static string? ReadDocumentationText(XElement element)
    {
        var text = element
            .Elements(BpmnNamespace + "documentation")
            .Select(doc => doc.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
    }

    /// <param name="support">
    /// The BPMN support manifest to validate against. Defaults to the embedded one;
    /// tests pass a perturbed manifest to prove validation follows the manifest
    /// rather than a list of its own.
    /// </param>
    public static WorkflowBpmnValidationResult ValidateProcess(
        string xml,
        BpmnSupportManifest? support = null)
    {
        try
        {
            var document = XDocument.Parse(xml);
            // #169. The primary process (see ResolvePrimaryProcess) -- a drawn-only
            // counterparty listed first must not be the one whose executability
            // decides the whole diagram.
            var processElement = ResolvePrimaryProcess(document);
            if (processElement is null)
            {
                return WorkflowBpmnValidationResult.WithError("The BPMN XML must contain a <process> element.");
            }

            var processKey = processElement.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(processKey))
            {
                return WorkflowBpmnValidationResult.WithError("The BPMN process must have a non-empty process key.");
            }

            var executable = processElement.Attribute("isExecutable")?.Value;
            if (!string.Equals(executable, "true", StringComparison.OrdinalIgnoreCase))
            {
                return WorkflowBpmnValidationResult.WithError("The BPMN process must be marked as executable before deployment.");
            }

            var errors = new List<string>();
            errors.AddRange(BuildScriptTaskValidationErrors(document));
            // #153: a script task whose identity cannot be determined must say
            // which one it means before it can be published.
            errors.AddRange(ScriptTaskIdentity.BuildIdentityValidationErrors(document));
            errors.AddRange(BuildSignalStartEventValidationErrors(document));
            errors.AddRange(BuildTimerStartEventValidationErrors(document));
            errors.AddRange(BuildTimerIntermediateCatchEventValidationErrors(document));
            errors.AddRange(BuildServiceTaskValidationErrors(document));
            errors.AddRange(BuildRecordTypeFilterMisplacementErrors(document));
            // #107: silence becomes a refusal with a reason.
            errors.AddRange(BuildUnsupportedElementErrors(document, support ?? BpmnSupportManifest.Default));
            // #169 supersedes #578's blanket refusal. A multi-pool diagram now deploys
            // one definition per executable pool, recorded as a set -- so what is
            // refused is a collaboration that could not deploy as a set: a
            // participant whose process does not exist, two processes sharing an
            // id, or no executable pool at all.
            errors.AddRange(BuildCollaborationErrors(document));
            // #170. A message flow that could never deliver is a send into the void.
            errors.AddRange(BuildMessageFlowErrors(document));
            // #111: a business rule task with no table decides nothing, and the
            // engine would refuse the deployment rather than say so usefully.
            errors.AddRange(BuildBusinessRuleTaskErrors(document));
            // #158: a conditional start event is legal only inside an event
            // subprocess. Flowable rejects it anywhere else with a parse error an
            // author cannot act on, so say what the constraint is instead.
            errors.AddRange(BuildStartEventPlacementErrors(document));
            // #316/#335: an event whose trigger does not resolve (error, both
            // triggers) or resolves to an unnamed root (error for signals, which
            // the engine refuses; warning for messages, which it deploys).
            var triggerFindings = BuildUnnamedEventTriggerFindings(document);
            errors.AddRange(triggerFindings.Errors);
            // #316: a send task the studio cannot configure and the engine refuses.
            errors.AddRange(BuildSendTaskErrors(document));
            // #333: elements the engine refuses -- or silently never runs -- for a
            // missing required attribute, in the state the palette leaves them.
            errors.AddRange(BuildMissingRequiredAttributeErrors(document));
            // #157: a timer boundary with no time set never fires.
            errors.AddRange(BuildTimerBoundaryEventValidationErrors(document));
            // #161: a subprocess the engine cannot enter.
            errors.AddRange(BuildSubProcessValidationErrors(document));

            // #115. The rules that used to live ONLY in ValidateStructureForPublish.
            //
            // #225 pointed publish at ValidateProcess, and because these were not
            // in it, that switch silently dropped three rules — including #114's
            // uncaught error code, whose failure mode is Flowable destroying the
            // whole instance with a 500 and no history. Nothing failed; the
            // promoted rules simply stopped running.
            //
            // One set now, so "the validation set" means one thing. Prepare gains
            // them too, which is where an author would rather meet them anyway.
            errors.AddRange(BuildAdhocSubProcessErrors(document));
            // The promoted structure rules, ONCE. They used to be listed here
            // individually as well as inside BuildStructureErrors, so an author
            // saw every one of these messages twice (#247).
            errors.AddRange(BuildStructureErrors(document));
            // #167: elements the studio converts away, and converted tasks nobody can do.
            errors.AddRange(BuildNonWaitingTaskErrors(document));
            // #164: a gateway that cannot be a choice, or points somewhere the
            // engine will not follow.
            // #162: an event subprocess that can never trigger.

            // #158: every condition in the diagram, through the one shared check.
            // Sequence flows included, so exclusive and inclusive gateways benefit
            // here rather than in a story of their own.
            var conditions = WorkflowConditionValidation.CheckDocument(document);
            errors.AddRange(conditions.Errors);

            var warnings = new List<string>();
            warnings.AddRange(triggerFindings.Warnings);
            warnings.AddRange(conditions.Warnings);
            warnings.AddRange(BuildGatewayWarnings(document));
            // #229: an interrupting handler that will not interrupt its siblings.
            warnings.AddRange(BuildSameScopeErrorHandlerWarnings(document));


            return new WorkflowBpmnValidationResult(errors, warnings);
        }
        catch (Exception exception)
        {
            return WorkflowBpmnValidationResult.WithError($"The BPMN XML is invalid: {exception.Message}");
        }
    }

    public static IReadOnlyList<string> ValidateExecutableProcess(string xml)
    {
        return ValidateProcess(xml).Errors;
    }

    // Publish-time warning rule: signal start events may carry a
    // `flowable:recordTypeShortCodes` filter referring to record types that
    // don't exist in the current DB (e.g. cross-environment exports). Publish
    // proceeds either way — the dispatcher will simply never match those
    // codes — but we surface the unresolved tokens so the operator can either
    // seed the type or fix the filter.
    public static async Task<IReadOnlyList<string>> BuildRecordTypeShortCodeWarningsAsync(
        string xml,
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch
        {
            // ValidateProcess will already have surfaced the parse error; no
            // additional warning to add for unparseable XML.
            return Array.Empty<string>();
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in document.Descendants(BpmnNamespace + "signalEventDefinition"))
        {
            var raw = def.Attribute(FlowableNamespace + "recordTypeShortCodes")?.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                referenced.Add(token);
            }
        }

        if (referenced.Count == 0)
        {
            return Array.Empty<string>();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = (await dbContext.RecordTypes
                .AsNoTracking()
                .Where(rt => referenced.Contains(rt.ShortCode))
                .Select(rt => rt.ShortCode)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = referenced
            .Where(code => !existing.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length == 0)
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            $"Signal start event references record-type shortcode(s) not found in this environment: {string.Join(", ", unknown)}. The filter will never match these until the type is created."
        };
    }

    public static string ExtractProcessKey(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants(BpmnNamespace + "process").FirstOrDefault()?.Attribute("id")?.Value
            ?? string.Empty;
    }

    public static string ExtractWorkflowName(string xml)
    {
        var document = XDocument.Parse(xml);
        var processElement = document.Descendants(BpmnNamespace + "process").FirstOrDefault();
        return processElement?.Attribute("name")?.Value
            ?? processElement?.Attribute("id")?.Value
            ?? string.Empty;
    }

    public static string NormalizeWorkflowName(string? workflowName)
    {
        var trimmed = workflowName?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Auton8 Workflow" : trimmed;
    }

    public static string NormalizeProcessKey(string? processKey)
    {
        var trimmed = processKey?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "autonate_workflow";
        }

        var sanitized = UnsafeProcessKeyCharactersRegex().Replace(trimmed, "_");
        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
        {
            sanitized = $"workflow_{sanitized}";
        }

        return sanitized;
    }

    public static string BuildDefaultWorkflowName()
    {
        return $"Auton8 Workflow {DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
    }

    public static string BuildDefaultProcessKey()
    {
        return $"autonate_workflow_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
    }

    public static string BuildProcessKeyForModel(string workflowName)
    {
        var normalizedName = NormalizeWorkflowName(workflowName);
        var slug = UnsafeProcessKeyCharactersRegex().Replace(normalizedName.ToLowerInvariant(), "_").Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "autonate_workflow";
        }

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        return NormalizeProcessKey($"{slug}_{uniqueSuffix}");
    }

    [GeneratedRegex("[^A-Za-z0-9_-]+", RegexOptions.Compiled)]
    private static partial Regex UnsafeProcessKeyCharactersRegex();

    private static void ApplyElementSnapshots(XDocument document, IReadOnlyCollection<WorkflowElementSnapshot> elementSnapshots)
    {
        if (elementSnapshots.Count == 0)
        {
            return;
        }

        var snapshotsById = elementSnapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Id))
            .GroupBy(snapshot => snapshot.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);

        var snapshotsByUniqueTaskName = elementSnapshots
            .Where(snapshot =>
                !string.IsNullOrWhiteSpace(snapshot.Name) &&
                ReplaceableTaskElementNames.Contains(ToBpmnLocalName(snapshot.Type) ?? string.Empty))
            .GroupBy(snapshot => snapshot.Name!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);

        foreach (var element in document.Descendants().Where(element => element.Name.Namespace == BpmnNamespace))
        {
            var id = element.Attribute("id")?.Value;
            var snapshot = default(WorkflowElementSnapshot);

            if (!string.IsNullOrWhiteSpace(id))
            {
                snapshotsById.TryGetValue(id, out snapshot);
            }

            if (snapshot is null &&
                ReplaceableTaskElementNames.Contains(element.Name.LocalName) &&
                !string.IsNullOrWhiteSpace(element.Attribute("name")?.Value))
            {
                snapshotsByUniqueTaskName.TryGetValue(element.Attribute("name")!.Value, out snapshot);
            }

            if (snapshot is null)
            {
                continue;
            }

            var targetLocalName = ToBpmnLocalName(snapshot.Type);
            if (!string.IsNullOrWhiteSpace(targetLocalName) &&
                ReplaceableTaskElementNames.Contains(element.Name.LocalName) &&
                ReplaceableTaskElementNames.Contains(targetLocalName) &&
                !string.Equals(element.Name.LocalName, targetLocalName, StringComparison.Ordinal))
            {
                element.Name = BpmnNamespace + targetLocalName;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Name))
            {
                element.SetAttributeValue("name", snapshot.Name);
            }

            if (string.Equals(targetLocalName, "scriptTask", StringComparison.Ordinal) ||
                string.Equals(element.Name.LocalName, "scriptTask", StringComparison.Ordinal))
            {
                ApplyScriptTaskSnapshot(element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "userTask", StringComparison.Ordinal))
            {
                ApplyUserTaskSnapshot(element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "sequenceFlow", StringComparison.Ordinal))
            {
                ApplySequenceFlowSnapshot(element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "businessRuleTask", StringComparison.Ordinal))
            {
                ApplyBusinessRuleTaskSnapshot(element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "startEvent", StringComparison.Ordinal) &&
                element.Element(BpmnNamespace + "signalEventDefinition") is not null)
            {
                ApplySignalStartEventSnapshot(document, element, snapshot);
            }

            // #524. EVERY message event, not just start events: the owner's
            // decision is "editable everywhere", and a boundary or intermediate
            // catch is exactly where a running instance waits for a name someone
            // outside has to know. Routed on the event definition rather than the
            // tag, so one branch covers startEvent, intermediateCatchEvent,
            // boundaryEvent and the throwing forms without four near-identical
            // conditions to keep in step.
            if (element.Element(BpmnNamespace + "messageEventDefinition") is not null)
            {
                ApplyMessageEventSnapshot(document, element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "startEvent", StringComparison.Ordinal) &&
                element.Element(BpmnNamespace + "timerEventDefinition") is not null)
            {
                ApplyTimerStartEventSnapshot(element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "intermediateCatchEvent", StringComparison.Ordinal) &&
                element.Element(BpmnNamespace + "timerEventDefinition") is not null)
            {
                ApplyTimerIntermediateCatchEventSnapshot(element, snapshot);
            }

            if (string.Equals(element.Name.LocalName, "serviceTask", StringComparison.Ordinal))
            {
                ApplyServiceTaskSnapshot(element, snapshot);
            }

            // #218. The routing script lives on the gateway the author drew, in
            // the same shape a script task uses, so the expansion has one place
            // to read it from and the stored diagram stays the author's.
            if (string.Equals(element.Name.LocalName, "complexGateway", StringComparison.Ordinal))
            {
                ApplyComplexGatewaySnapshot(element, snapshot);
            }

            // #158. Not `else if` — a boundary event is both a conditional event
            // and, potentially, something else the chain above handled.
            if (element.Element(BpmnNamespace + "conditionalEventDefinition") is not null)
            {
                ApplyConditionalEventSnapshot(element, snapshot);
            }

            // #157: a timer boundary event. Matched on boundaryEvent PLUS a timer
            // definition so it cannot claim the conditional boundary events above,
            // nor the timer start and intermediate catch handlers earlier.
            if (string.Equals(element.Name.LocalName, "boundaryEvent", StringComparison.Ordinal) &&
                element.Element(BpmnNamespace + "timerEventDefinition") is not null)
            {
                ApplyTimerBoundaryEventSnapshot(element, snapshot);
            }
        }
    }

    // #158: the condition an author wrote, plus whether a boundary event
    // interrupts.
    //
    // One handler for all three placements — intermediate catch, boundary, and the
    // event-subprocess start (#162) — because the condition is the same element in
    // each and BPMN spells it the same way. `cancelActivity` is meaningful only on
    // a boundary event; writing it elsewhere would be noise in the XML.
    private static void ApplyConditionalEventSnapshot(XElement eventElement, WorkflowElementSnapshot snapshot)
    {
        var definition = eventElement.Element(BpmnNamespace + "conditionalEventDefinition");
        if (definition is null)
        {
            return;
        }

        if (snapshot.ConditionExpression is not null)
        {
            var condition = definition.Element(BpmnNamespace + "condition");
            if (string.IsNullOrWhiteSpace(snapshot.ConditionExpression))
            {
                // An empty condition is refused at publish rather than written as
                // an empty element that never evaluates.
                condition?.Remove();
            }
            else
            {
                if (condition is null)
                {
                    condition = new XElement(BpmnNamespace + "condition");
                    definition.Add(condition);
                }

                // Flowable reads the condition body; the xsi:type is what marks it
                // a formal expression, exactly as sequence flow conditions do.
                condition.SetAttributeValue(XsiNamespace + "type", FormalExpressionType(condition));
                condition.Value = snapshot.ConditionExpression;
            }
        }

        if (snapshot.CancelActivity is { } cancelActivity
            && string.Equals(eventElement.Name.LocalName, "boundaryEvent", StringComparison.Ordinal))
        {
            // Written explicitly in both directions. BPMN defaults cancelActivity
            // to true when absent, so leaving it off to mean "interrupting" would
            // make a non-interrupting event impossible to turn back.
            eventElement.SetAttributeValue("cancelActivity", cancelActivity ? "true" : "false");
        }
    }

    // Service tasks are routed to AutoNate via a fixed Flowable bean —
    // `autonateBehaviorDelegate`, registered in the flowable-extension Spring
    // autoconfig. The author's choice of behavior is stored as plain
    // flowable: attributes on the serviceTask element (the studio's bpmn-js
    // doesn't load a Flowable moddle extension, so attributes are the only
    // round-trip-safe shape — same pattern used for assignee/dueDate/topic).
    // A second attribute `autonateServiceKind` is reserved for future
    // service-task types (HTTP webhook, etc.) so adding them later doesn't
    // require an XML migration on existing models.
    private const string AutoNateBehaviorDelegateExpression = "${autonateBehaviorDelegate}";

    // #112. The built-in behaviour every expanded message-throwing element is
    // wired to. Kept as a literal here rather than referencing the behaviour type,
    // so this file stays free of a dependency on the behaviours namespace; a test
    // pins the two together.
    private const string SendMessageBehaviorKey = "autonate.send-message";
    private const string ServiceTaskBehaviorKind = "behavior";

    private static void ApplyServiceTaskSnapshot(XElement serviceTaskElement, WorkflowElementSnapshot snapshot)
    {
        var trimmedKey = snapshot.BehaviorKey?.Trim();
        var kind = string.IsNullOrWhiteSpace(snapshot.ServiceTaskKind)
            ? ServiceTaskBehaviorKind
            : snapshot.ServiceTaskKind!.Trim();

        // Strip alternative wirings before installing ours so a service task
        // round-tripped from another modeler can't end up referencing both a
        // delegate expression and a class. Sweep both flowable:-prefixed
        // and plain (no-namespace) attributes — an older SPA build wrote
        // `delegateExpression` without a prefix via bpmn-js's typed property
        // API, which the BPMN core schema rejects on deploy.
        serviceTaskElement.SetAttributeValue(FlowableNamespace + "class", null);
        serviceTaskElement.SetAttributeValue(FlowableNamespace + "expression", null);
        serviceTaskElement.SetAttributeValue(FlowableNamespace + "type", null);
        serviceTaskElement.SetAttributeValue("class", null);
        serviceTaskElement.SetAttributeValue("expression", null);
        serviceTaskElement.SetAttributeValue("type", null);
        serviceTaskElement.SetAttributeValue("delegateExpression", null);

        serviceTaskElement.SetAttributeValue(FlowableNamespace + "delegateExpression", AutoNateBehaviorDelegateExpression);
        // Default true matches Flowable's behavior; setting it explicitly
        // protects against modeler regressions that drop the attribute.
        serviceTaskElement.SetAttributeValue(FlowableNamespace + "exclusive", "true");

        serviceTaskElement.SetAttributeValue(FlowableNamespace + "autonateServiceKind", kind);
        if (string.IsNullOrEmpty(trimmedKey))
        {
            serviceTaskElement.SetAttributeValue(FlowableNamespace + "behaviorKey", null);
        }
        else
        {
            serviceTaskElement.SetAttributeValue(FlowableNamespace + "behaviorKey", trimmedKey);
        }

        // #168. The retry point. flowable:async makes the step its own
        // transaction boundary: the engine commits before running it, so a
        // failure produces a retryable job for that step alone instead of
        // rolling back to the last checkpoint. Verified against Flowable 8.0.0
        // — unmarked, a failing step rolls the whole start back and no instance
        // survives; marked, the preceding steps stay recorded and the failure
        // lands in the dead-letter table with its exception.
        //
        // Only written when the studio said so. A null RetryPoint means the
        // snapshot came from a build that does not know about the setting, and
        // leaving the attribute untouched keeps that build from clearing it.
        if (snapshot.RetryPoint is { } retryPoint)
        {
            serviceTaskElement.SetAttributeValue(
                FlowableNamespace + "async", retryPoint ? "true" : null);
        }

        // Sweep any leftover field-injection children from the previous
        // implementation so XML produced by an older studio build round-trips
        // cleanly under the new attribute shape.
        StripLegacyServiceTaskFields(serviceTaskElement);
    }

    private static void StripLegacyServiceTaskFields(XElement serviceTaskElement)
    {
        var extensionElements = serviceTaskElement.Element(BpmnNamespace + "extensionElements");
        if (extensionElements is null) return;

        var stale = extensionElements
            .Elements(FlowableNamespace + "field")
            .Where(field =>
            {
                var name = field.Attribute("name")?.Value;
                return string.Equals(name, "autonateServiceKind", StringComparison.Ordinal) ||
                       string.Equals(name, "behaviorKey", StringComparison.Ordinal);
            })
            .ToArray();
        foreach (var field in stale)
        {
            field.Remove();
        }

        if (!extensionElements.HasElements && !extensionElements.HasAttributes)
        {
            extensionElements.Remove();
        }
    }

    private static void ApplyTimerIntermediateCatchEventSnapshot(XElement catchEventElement, WorkflowElementSnapshot snapshot)
    {
        var timerEventDefinition = catchEventElement.Element(BpmnNamespace + "timerEventDefinition");
        if (timerEventDefinition is null)
        {
            return;
        }

        var trimmedDuration = snapshot.TimerDuration?.Trim();
        var trimmedDate = snapshot.TimerDate?.Trim();

        // Intermediate catch timers fire once. Strip every kind first so a
        // mode switch (duration ⇄ date) can't leave the previous child behind
        // — Flowable rejects a timerEventDefinition with multiple kinds.
        timerEventDefinition.Elements(BpmnNamespace + "timeCycle").Remove();
        timerEventDefinition.Elements(BpmnNamespace + "timeDuration").Remove();
        timerEventDefinition.Elements(BpmnNamespace + "timeDate").Remove();

        if (!string.IsNullOrEmpty(trimmedDuration))
        {
            timerEventDefinition.Add(new XElement(BpmnNamespace + "timeDuration", trimmedDuration));
        }
        else if (!string.IsNullOrEmpty(trimmedDate))
        {
            timerEventDefinition.Add(new XElement(BpmnNamespace + "timeDate", trimmedDate));
        }
    }

    private static void ApplyTimerStartEventSnapshot(XElement startEventElement, WorkflowElementSnapshot snapshot)
    {
        var timerEventDefinition = startEventElement.Element(BpmnNamespace + "timerEventDefinition");
        if (timerEventDefinition is null)
        {
            return;
        }

        var trimmedCron = snapshot.TimerCycleCron?.Trim();

        // Strip the alternative timer kinds — bpmn-js leaves them around when the
        // user switches modes, and Flowable rejects a timerEventDefinition with
        // multiple kind children.
        timerEventDefinition.Elements(BpmnNamespace + "timeDate").Remove();
        timerEventDefinition.Elements(BpmnNamespace + "timeDuration").Remove();

        var timeCycle = timerEventDefinition.Element(BpmnNamespace + "timeCycle");
        if (string.IsNullOrEmpty(trimmedCron))
        {
            timeCycle?.Remove();
        }
        else
        {
            if (timeCycle is null)
            {
                timeCycle = new XElement(BpmnNamespace + "timeCycle");
                timerEventDefinition.Add(timeCycle);
            }
            timeCycle.SetAttributeValue(FlowableNamespace + "type", "cron");
            timeCycle.Value = trimmedCron;
        }

        // Persist endDate as an attribute on timerEventDefinition so it
        // round-trips cleanly through bpmn-moddle on the SPA side (unknown
        // child elements get stripped without a schema descriptor; attributes
        // survive via $attrs). Drop any pre-existing child variant in case
        // the user pasted XML that used the documented child-element form.
        timerEventDefinition.Elements(FlowableNamespace + "endDate").Remove();
        var trimmedEndDate = snapshot.TimerEndDate?.Trim();
        if (string.IsNullOrEmpty(trimmedEndDate))
        {
            timerEventDefinition.SetAttributeValue(FlowableNamespace + "endDate", null);
        }
        else
        {
            timerEventDefinition.SetAttributeValue(FlowableNamespace + "endDate", trimmedEndDate);
        }
    }

    private static void ApplySignalStartEventSnapshot(XDocument document, XElement startEventElement, WorkflowElementSnapshot snapshot)
    {
        var signalEventDefinition = startEventElement.Element(BpmnNamespace + "signalEventDefinition");
        if (signalEventDefinition is null)
        {
            return;
        }

        var trimmedSignalName = snapshot.SignalName?.Trim();
        if (string.IsNullOrEmpty(trimmedSignalName))
        {
            // bpmn-js leaves the event with an unresolved signalRef when the
            // user hasn't picked a name. Strip the signalRef so the XML at
            // least parses cleanly; validation will surface the missing name.
            // Also clear any record-type filter so a stale value doesn't
            // linger on the now-broken event (it's bound to a signal that no
            // longer exists).
            signalEventDefinition.SetAttributeValue("signalRef", null);
            signalEventDefinition.SetAttributeValue(FlowableNamespace + "recordTypeShortCodes", null);
            return;
        }

        var trimmedTopic = string.IsNullOrWhiteSpace(snapshot.SignalTopic)
            ? DefaultSignalTopic
            : snapshot.SignalTopic.Trim();

        var definitionsElement = document.Root!;
        var signal = ResolveOrCreateSignalRoot(definitionsElement, trimmedSignalName);
        signal.SetAttributeValue("name", trimmedSignalName);
        signal.SetAttributeValue(FlowableNamespace + "topic", trimmedTopic);

        signalEventDefinition.SetAttributeValue("signalRef", signal.Attribute("id")!.Value);

        // Per-event record-type filter. Empty/null clears the attribute (preserves
        // "match all records" behavior). Non-empty writes a comma-joined list.
        var shortCodes = snapshot.RecordTypeShortCodes;
        if (shortCodes is null || shortCodes.Count == 0)
        {
            signalEventDefinition.SetAttributeValue(
                FlowableNamespace + "recordTypeShortCodes", null);
        }
        else
        {
            var normalized = string.Join(",", shortCodes
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()));
            signalEventDefinition.SetAttributeValue(
                FlowableNamespace + "recordTypeShortCodes",
                string.IsNullOrEmpty(normalized) ? null : normalized);
        }
    }

    /// <summary>
    /// The studio names a message event's message, and owns the declaration (#524).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This reverses a deliberate decision, and the reason it reverses cleanly is
    /// the mechanism. The field was disabled because the name "comes from the
    /// <c>&lt;bpmn:message&gt;</c> the diagram declares, so letting it be typed
    /// would let it diverge from what the engine subscribes to." True — as long
    /// as typing it wrote only the event. Writing the ROOT as well removes the
    /// divergence by construction rather than by scope, which is the owner's call.
    /// </para>
    /// <para>
    /// Done in <c>prepare</c> rather than in the studio's own JavaScript for the
    /// same reason the complex gateway's routing script lives in an
    /// <c>autonate:</c> attribute: bpmn-js is vendored with no Flowable moddle
    /// extension, and a root element the studio synthesised would meet the same
    /// serialiser that drops what its moddle does not know.
    /// </para>
    /// <para>
    /// A blank name clears <c>messageRef</c> rather than guessing, matching the
    /// signal path exactly: the XML stays parseable and validation surfaces the
    /// missing name, instead of the event silently keeping a stale subscription.
    /// </para>
    /// </remarks>
    private static void ApplyMessageEventSnapshot(
        XDocument document, XElement element, WorkflowElementSnapshot snapshot)
    {
        var messageEventDefinition = element.Element(BpmnNamespace + "messageEventDefinition");
        if (messageEventDefinition is null)
        {
            return;
        }

        // Absent means "an older SPA build did not send this", which must not
        // clear a name somebody set. Empty means the author cleared it.
        if (snapshot.MessageName is null)
        {
            return;
        }

        var trimmed = snapshot.MessageName.Trim();
        if (trimmed.Length == 0)
        {
            messageEventDefinition.SetAttributeValue("messageRef", null);
            return;
        }

        var message = ResolveOrCreateMessageRoot(document.Root!, trimmed);
        message.SetAttributeValue("name", trimmed);
        messageEventDefinition.SetAttributeValue("messageRef", message.Attribute("id")!.Value);
    }

    /// <summary>The &lt;bpmn:message&gt; root for a name, created if absent (#524).</summary>
    /// <remarks>
    /// Deliberately the same shape as <see cref="ResolveOrCreateSignalRoot"/>,
    /// including the collision counter and the schema-ordering insert: messages,
    /// like signals, must precede <c>&lt;process&gt;</c>, and appending one would
    /// produce XML the engine refuses.
    /// </remarks>
    private static XElement ResolveOrCreateMessageRoot(XElement definitionsElement, string messageName)
    {
        var existing = definitionsElement
            .Elements(BpmnNamespace + "message")
            .FirstOrDefault(element =>
                string.Equals(element.Attribute("name")?.Value, messageName, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var id = $"Message_{Math.Abs(messageName.GetHashCode(StringComparison.Ordinal)):X}";
        var counter = 1;
        var finalId = id;
        while (definitionsElement.Elements(BpmnNamespace + "message")
                   .Any(m => string.Equals(m.Attribute("id")?.Value, finalId, StringComparison.Ordinal)))
        {
            finalId = $"{id}_{counter++}";
        }

        var message = new XElement(BpmnNamespace + "message", new XAttribute("id", finalId));
        var firstProcess = definitionsElement.Elements(BpmnNamespace + "process").FirstOrDefault();
        if (firstProcess is not null)
        {
            firstProcess.AddBeforeSelf(message);
        }
        else
        {
            definitionsElement.Add(message);
        }

        return message;
    }

    private static XElement ResolveOrCreateSignalRoot(XElement definitionsElement, string signalName)
    {
        var existing = definitionsElement
            .Elements(BpmnNamespace + "signal")
            .FirstOrDefault(element =>
                string.Equals(element.Attribute("name")?.Value, signalName, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var id = $"Signal_{Math.Abs(signalName.GetHashCode(StringComparison.Ordinal)):X}";
        // Disambiguate if the slug collides with an unrelated existing signal.
        var counter = 1;
        var finalId = id;
        while (definitionsElement.Elements(BpmnNamespace + "signal")
                   .Any(s => string.Equals(s.Attribute("id")?.Value, finalId, StringComparison.Ordinal)))
        {
            finalId = $"{id}_{counter++}";
        }

        var signal = new XElement(BpmnNamespace + "signal", new XAttribute("id", finalId));
        // Signals must come before <process> in the BPMN schema. Insert at the
        // top of the definitions element to keep the document valid.
        var firstProcess = definitionsElement.Elements(BpmnNamespace + "process").FirstOrDefault();
        if (firstProcess is not null)
        {
            firstProcess.AddBeforeSelf(signal);
        }
        else
        {
            definitionsElement.Add(signal);
        }

        return signal;
    }

    // Removes <bpmn:signal> roots that are no longer referenced by any
    // signalEventDefinition in the document. Runs before the per-snapshot
    // apply step so renamed/cleared signals don't leak stale root entries.
    private static void PruneOrphanSignalRoots(XDocument document)
    {
        var referencedIds = document
            .Descendants(BpmnNamespace + "signalEventDefinition")
            .Select(definition => definition.Attribute("signalRef")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = document.Root!
            .Elements(BpmnNamespace + "signal")
            .Where(signal =>
            {
                var id = signal.Attribute("id")?.Value;
                return string.IsNullOrWhiteSpace(id) || !referencedIds.Contains(id);
            })
            .ToArray();

        foreach (var orphan in orphans)
        {
            orphan.Remove();
        }
    }

    // Reads (signalName, topic, processDefinitionKey, recordTypeShortCodes)
    // tuples for every signal start event in the document. Used by the
    // runtime registry to know which Dapr topics to subscribe on, which
    // signal names to dispatch, and which workflow each one starts.
    // Throws System.Xml.XmlException on malformed XML. Callers that iterate
    // many workflows should catch per-workflow so one bad model doesn't sink
    // the rest of the index (see EfCoreWorkflowSignalRegistry.RefreshAsync).
    // #112. The correlation key lives on the element, in the flowable namespace
    // under an autonate name — the same shape as autonateServiceKind and
    // autonateConvertedFrom, and for the reason #167 found the hard way: a diagram
    // reliably declares xmlns:flowable, and bpmn-moddle silently discards an
    // attribute whose prefix is undeclared.
    internal const string CorrelationKeyAttribute = "autonateCorrelationKey";

    internal const string CalledElementTypeAttribute = "calledElementType";

    /// <summary>
    /// The process keys this diagram's call activities target (#113).
    /// </summary>
    /// <remarks>
    /// Keys only — a diagram already carrying pinned definition ids (because it
    /// was round-tripped from a deployed copy) is left alone, since re-pinning it
    /// would silently move it to a newer child.
    /// </remarks>
    public static IReadOnlyList<(string ElementId, string CalledKey)> ExtractCallActivityTargets(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<(string, string)>();

        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return Array.Empty<(string, string)>(); }

        var targets = new List<(string, string)>();
        foreach (var call in document.Descendants(BpmnNamespace + "callActivity"))
        {
            var elementId = call.Attribute("id")?.Value;
            var calledElement = call.Attribute("calledElement")?.Value;
            if (string.IsNullOrWhiteSpace(elementId) || string.IsNullOrWhiteSpace(calledElement)) continue;

            // Already pinned by a previous publish; not a key to resolve again.
            if (string.Equals(
                    call.Attribute(FlowableNamespace + CalledElementTypeAttribute)?.Value,
                    "id", StringComparison.Ordinal))
            {
                continue;
            }

            targets.Add((elementId!, calledElement!.Trim()));
        }

        return targets;
    }

    /// <summary>
    /// Rewrites each call activity to the exact child definition that exists now
    /// (#113), so republishing the child cannot change what an already-deployed
    /// parent calls.
    /// </summary>
    /// <remarks>
    /// Flowable resolves a `calledElement` KEY at run time, to the latest version
    /// — verified: an unchanged parent picked up a child version deployed after
    /// it. This issue decided the opposite, because a running process must not
    /// change behaviour underneath its owner.
    ///
    /// Pinning also bounds recursion by construction. A parent can only pin to a
    /// definition that already exists, so every call points strictly backwards in
    /// deployment order and the chain must terminate. A process whose first
    /// version calls itself has nothing to resolve and is refused; a later version
    /// pins to the earlier one, which is finite.
    ///
    /// Applied to the DEPLOYED copy only. The stored diagram keeps the key the
    /// author picked, which is what the studio shows them.
    /// </remarks>
    public static string PinCallActivityTargets(
        string xml, IReadOnlyDictionary<string, string> definitionIdsByKey)
    {
        if (string.IsNullOrWhiteSpace(xml) || definitionIdsByKey.Count == 0) return xml;

        var document = XDocument.Parse(xml);
        foreach (var call in document.Descendants(BpmnNamespace + "callActivity"))
        {
            var calledElement = call.Attribute("calledElement")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(calledElement)) continue;
            if (string.Equals(
                    call.Attribute(FlowableNamespace + CalledElementTypeAttribute)?.Value,
                    "id", StringComparison.Ordinal))
            {
                continue;
            }

            if (!definitionIdsByKey.TryGetValue(calledElement!, out var definitionId)) continue;

            call.SetAttributeValue("calledElement", definitionId);
            call.SetAttributeValue(FlowableNamespace + CalledElementTypeAttribute, "id");
        }

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    internal const string TargetProcessKeyAttribute = "autonateTargetProcessKey";

    /// <summary>
    /// Every point in a definition that sends a message (#112).
    /// </summary>
    public static IReadOnlyList<WorkflowMessageSendDeclaration> ExtractMessageSendDeclarations(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<WorkflowMessageSendDeclaration>();
        }

        var document = XDocument.Parse(xml);
        var messageNamesById = MessageNamesById(document);
        var declarations = new List<WorkflowMessageSendDeclaration>();

        foreach (var element in document.Descendants())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;

            var localName = element.Name.LocalName;
            var isThrow = localName is "intermediateThrowEvent" or "endEvent";
            var isSendTask = localName == "sendTask";
            if (!isThrow && !isSendTask) continue;

            var elementId = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(elementId)) continue;

            // A throw event only counts when it actually carries a message
            // definition — a plain end event is not a send, and #167 already
            // established that a bare throw-none passes straight through.
            string messageName;
            if (isThrow)
            {
                var definition = element.Elements(BpmnNamespace + "messageEventDefinition").FirstOrDefault();
                if (definition is null) continue;

                var messageRef = definition.Attribute("messageRef")?.Value;
                messageName = messageRef is not null && messageNamesById.TryGetValue(messageRef, out var resolved)
                    ? resolved
                    : string.Empty;
            }
            else
            {
                // A send task names its message on the element, since it has no
                // event definition to hang one on.
                messageName = element.Attribute(FlowableNamespace + "autonateMessageName")?.Value ?? string.Empty;
            }

            // #170. A message flow drawn from this element IS its addressing.
            // Prepare stamps the attributes for the studio's benefit, but a
            // diagram published as raw XML never went through prepare, and the
            // flow is the fact either way -- so it is resolved here too, with an
            // explicit attribute always winning over the drawn flow.
            var target = Trimmed(element.Attribute(FlowableNamespace + TargetProcessKeyAttribute)?.Value);
            var key = Trimmed(element.Attribute(FlowableNamespace + CorrelationKeyAttribute)?.Value);
            var flowTarget = ResolveMessageFlowTarget(document, element, messageNamesById);
            if (flowTarget is not null)
            {
                target ??= flowTarget.Value.ProcessId;
                key ??= flowTarget.Value.CorrelationKey;
                if (string.IsNullOrWhiteSpace(messageName)) messageName = flowTarget.Value.MessageName;
            }

            declarations.Add(new WorkflowMessageSendDeclaration(
                elementId,
                messageName.Trim(),
                target,
                key,
                EndsProcess: localName == "endEvent"));
        }

        return declarations;
    }

    private static bool IsInsideEventSubProcess(XElement element) =>
        element.Ancestors(BpmnNamespace + "subProcess")
            .Any(sp => string.Equals(
                sp.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase));

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> MessageNamesById(XDocument document) =>
        document.Root?
            .Elements(BpmnNamespace + "message")
            .Where(message => !string.IsNullOrWhiteSpace(message.Attribute("id")?.Value))
            .ToDictionary(
                message => message.Attribute("id")!.Value,
                message => message.Attribute("name")?.Value ?? string.Empty,
                StringComparer.Ordinal)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The signal names this definition CATCHES, by name (#523).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Catches only: <c>startEvent</c>, <c>boundaryEvent</c> and
    /// <c>intermediateCatchEvent</c>. A signal <b>throw</b> — an
    /// <c>endEvent</c> or <c>intermediateThrowEvent</c> — is deliberately
    /// excluded. A workflow that only raises a name is not waiting for it, and
    /// counting it as a declaration would make "nothing declares this signal"
    /// unreachable for any name already in use, which is the refusal AC4 asks
    /// for.
    /// </para>
    /// <para>
    /// <b>Why not <c>IWorkflowSignalRegistry</c>.</b> That registry holds signal
    /// START events, because its job is deciding which Dapr topics to subscribe
    /// to. Refusing against it alone would refuse a name caught only by a
    /// boundary or intermediate event — a running instance waiting on a signal,
    /// which is precisely what a broadcast is for.
    /// </para>
    /// <para>
    /// A <c>signalRef</c> pointing at nothing is skipped rather than guessed at,
    /// the same rule <see cref="ExtractMessageDeclarations"/> applies to
    /// <c>messageRef</c>: the engine subscribes under the signal's NAME, so an
    /// unresolvable reference is not addressable.
    /// </para>
    /// </remarks>
    public static IReadOnlyCollection<string> ExtractCaughtSignalNames(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<string>();
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch
        {
            // An unparseable stored diagram is somebody else's finding; this
            // answers "declares nothing" rather than throwing at a caller who
            // asked about a different workflow.
            return Array.Empty<string>();
        }

        var signalNamesById = document.Root?
            .Elements(BpmnNamespace + "signal")
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Attribute("id")?.Value))
            .ToDictionary(
                signal => signal.Attribute("id")!.Value,
                signal => signal.Attribute("name")?.Value ?? string.Empty,
                StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var caught = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in document.Descendants())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;

            var catches = element.Name.LocalName is
                "startEvent" or "boundaryEvent" or "intermediateCatchEvent";
            if (!catches) continue;

            var definition = element.Elements(BpmnNamespace + "signalEventDefinition").FirstOrDefault();
            if (definition is null) continue;

            var signalRef = definition.Attribute("signalRef")?.Value;
            if (string.IsNullOrWhiteSpace(signalRef)) continue;
            if (!signalNamesById.TryGetValue(signalRef, out var name)) continue;
            if (string.IsNullOrWhiteSpace(name)) continue;

            caught.Add(name);
        }

        return caught;
    }

    /// <summary>
    /// Every point in a published definition that can be advanced from outside,
    /// with the variable that addresses it (#112).
    /// </summary>
    public static IReadOnlyList<WorkflowMessageDeclaration> ExtractMessageDeclarations(string xml) =>
        ExtractMessageDeclarations(xml, processId: null);

    /// <summary>
    /// The message-catching points of ONE process in a diagram that may hold
    /// several (#170). Null scopes to the whole document, which is what a
    /// single-process diagram always was.
    /// </summary>
    /// <remarks>
    /// After #169 a published workflow can carry N definitions, and a message
    /// addressed to the Seller pool must be answered by Seller's declarations
    /// alone -- a receive task in the SENDER's pool with the same name is not a
    /// match, it is the bug this overload exists to prevent.
    /// </remarks>
    public static IReadOnlyList<WorkflowMessageDeclaration> ExtractMessageDeclarations(string xml, string? processId)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<WorkflowMessageDeclaration>();
        }

        var document = XDocument.Parse(xml);

        // #170. Scoping exists to tell one pool's declarations from another's, so
        // it applies only where there is more than one process to tell apart AND
        // the addressed id names one of them. A single-process diagram answers
        // for any key it is addressed by -- which is what every caller before
        // this overload relied on, including stores whose stored key and process
        // id have drifted apart.
        var processes = document.Descendants(BpmnNamespace + "process").ToList();
        var scopeToProcess = processId is not null
            && processes.Count > 1
            && processes.Any(p => p.Attribute("id")?.Value == processId);

        // <bpmn:message id="…" name="…"> lives at definitions level; the events
        // reference it by id. The name is what the engine subscribes under, so a
        // messageRef pointing at nothing is not addressable and is skipped rather
        // than guessed at.
        var messageNamesById = MessageNamesById(document);

        var declarations = new List<WorkflowMessageDeclaration>();

        foreach (var element in document.Descendants())
        {
            var localName = element.Name.LocalName;
            var kind = localName switch
            {
                // #162. A start event inside an EVENT SUBPROCESS starts that
                // handler within an already-running instance — it is a catch, not
                // a way to start a process. Classifying it as Start made the
                // correlator try to start a brand-new instance by message, which
                // Flowable refuses ("no subscription to message with name …")
                // because no process-level start event carries it. Found by #162's
                // message-handler test; a defect in #112 as shipped.
                "startEvent" when IsInsideEventSubProcess(element) => WorkflowMessageTargetKind.Catch,
                "startEvent" => WorkflowMessageTargetKind.Start,
                "intermediateCatchEvent" or "boundaryEvent" => WorkflowMessageTargetKind.Catch,
                "receiveTask" => WorkflowMessageTargetKind.ReceiveTask,
                _ => (WorkflowMessageTargetKind?)null
            };
            if (kind is null || element.Name.Namespace != BpmnNamespace) continue;
            if (scopeToProcess
                && element.Ancestors(BpmnNamespace + "process").FirstOrDefault()?.Attribute("id")?.Value != processId)
            {
                continue;
            }

            var elementId = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(elementId)) continue;

            var correlationKey = element.Attribute(FlowableNamespace + CorrelationKeyAttribute)?.Value;
            correlationKey = string.IsNullOrWhiteSpace(correlationKey) ? null : correlationKey.Trim();

            if (kind == WorkflowMessageTargetKind.ReceiveTask)
            {
                // A receive task has no message element. It is still addressable,
                // by its own id, so it belongs in this list — leaving it out is how
                // "a receive task is a process that stops forever" happens.
                declarations.Add(new WorkflowMessageDeclaration(
                    elementId, kind.Value, string.Empty, correlationKey));
                continue;
            }

            var definition = element.Elements(BpmnNamespace + "messageEventDefinition").FirstOrDefault();
            if (definition is null) continue;

            var messageRef = definition.Attribute("messageRef")?.Value;
            if (string.IsNullOrWhiteSpace(messageRef)
                || !messageNamesById.TryGetValue(messageRef, out var messageName)
                || string.IsNullOrWhiteSpace(messageName))
            {
                continue;
            }

            declarations.Add(new WorkflowMessageDeclaration(
                elementId,
                kind.Value,
                messageName,
                // A start event has nothing to correlate to — no instance exists
                // yet — so any key written on one is ignored rather than honoured,
                // which would otherwise look like a filter that silently matches
                // everything.
                kind == WorkflowMessageTargetKind.Start ? null : correlationKey));
        }

        return declarations;
    }

    /// <summary>
    /// Every message START event a published definition offers the bus (#524).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A start event inside an EVENT SUB-PROCESS is excluded, and that is #162's
    /// lesson borrowed rather than rediscovered: such an event starts a handler
    /// within an already-running instance, so treating it as a way to start a
    /// process makes the correlator ask Flowable to start one by a message no
    /// process-level start event carries, and the engine refuses.
    /// </para>
    /// <para>
    /// The topic lives on the <c>&lt;bpmn:message&gt;</c> root, like a signal's,
    /// so an author who wants a message on their own topic says it the same way.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<WorkflowMessageRegistration> ExtractMessageRegistrations(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<WorkflowMessageRegistration>();
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch
        {
            return Array.Empty<WorkflowMessageRegistration>();
        }

        var messagesById = document.Root?
            .Elements(BpmnNamespace + "message")
            .Where(message => !string.IsNullOrWhiteSpace(message.Attribute("id")?.Value))
            .ToDictionary(
                message => message.Attribute("id")!.Value,
                message => message,
                StringComparer.Ordinal)
            ?? new Dictionary<string, XElement>(StringComparer.Ordinal);

        var registrations = new Dictionary<(string Name, string Topic, string ProcessKey), WorkflowMessageRegistration>();

        foreach (var startEvent in document.Descendants(BpmnNamespace + "startEvent"))
        {
            if (IsInsideEventSubProcess(startEvent)) continue;

            var definition = startEvent.Element(BpmnNamespace + "messageEventDefinition");
            if (definition is null) continue;

            var messageRef = definition.Attribute("messageRef")?.Value;
            if (string.IsNullOrWhiteSpace(messageRef)
                || !messagesById.TryGetValue(messageRef, out var message))
            {
                continue;
            }

            var name = message.Attribute("name")?.Value?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var topic = message.Attribute(FlowableNamespace + "topic")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                topic = DefaultMessageTopic;
            }

            var processKey = startEvent.Ancestors(BpmnNamespace + "process")
                .FirstOrDefault()?.Attribute("id")?.Value?.Trim();
            if (string.IsNullOrEmpty(processKey)) continue;

            registrations[(name, topic, processKey)] =
                new WorkflowMessageRegistration(name, topic, processKey);
        }

        return registrations.Values.ToList();
    }

    public static IReadOnlyList<WorkflowSignalRegistration> ExtractSignalRegistrations(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<WorkflowSignalRegistration>();
        }

        var document = XDocument.Parse(xml);

        var signalsById = document.Root?
            .Elements(BpmnNamespace + "signal")
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Attribute("id")?.Value))
            .ToDictionary(
                signal => signal.Attribute("id")!.Value,
                signal => signal,
                StringComparer.Ordinal)
            ?? new Dictionary<string, XElement>(StringComparer.Ordinal);

        var registrations = new Dictionary<(string Name, string Topic, string ProcessKey), WorkflowSignalRegistration>();

        foreach (var startEvent in document.Descendants(BpmnNamespace + "startEvent"))
        {
            var signalEventDefinition = startEvent.Element(BpmnNamespace + "signalEventDefinition");
            if (signalEventDefinition is null)
            {
                continue;
            }

            var signalRef = signalEventDefinition.Attribute("signalRef")?.Value;
            if (string.IsNullOrWhiteSpace(signalRef) || !signalsById.TryGetValue(signalRef, out var signal))
            {
                continue;
            }

            var name = signal.Attribute("name")?.Value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var topic = signal.Attribute(FlowableNamespace + "topic")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                topic = DefaultSignalTopic;
            }

            // Walk up to the enclosing <process id="..."> — that is the
            // processDefinitionKey Flowable will use when starting an instance.
            var processElement = startEvent.Ancestors(BpmnNamespace + "process").FirstOrDefault();
            var processKey = processElement?.Attribute("id")?.Value?.Trim();
            if (string.IsNullOrEmpty(processKey))
            {
                continue;
            }

            var shortCodesAttr = signalEventDefinition.Attribute(FlowableNamespace + "recordTypeShortCodes")?.Value;
            var shortCodes = ParseShortCodeList(shortCodesAttr);

            var key = (name, topic, processKey);
            registrations.TryAdd(key, new WorkflowSignalRegistration(name, topic, processKey, shortCodes));
        }

        return registrations.Values.ToArray();
    }

    private static IReadOnlySet<string> ParseShortCodeList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return EmptyShortCodeSet;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            set.Add(token);
        }

        // Freeze before returning so registrations can't be mutated by downstream
        // consumers that might cache the set across async boundaries.
        return set.ToFrozenSet(StringComparer.Ordinal);
    }

    private static readonly IReadOnlySet<string> EmptyShortCodeSet =
        FrozenSet<string>.Empty;

    /// <summary>
    /// The decision table an author chose reaches the STORED diagram (#111).
    /// </summary>
    /// <remarks>
    /// An <c>autonate:</c> attribute, which is the only shape proven to survive a
    /// bpmn-js round trip — the studio loads no Flowable moddle extension, so a
    /// typed property or a child element is silently dropped on the author's next
    /// save. <c>ExpandBusinessRuleTasks</c> moves it into the engine's field
    /// extension at publish and strips it from the deployed copy.
    /// </remarks>
    private static void ApplyBusinessRuleTaskSnapshot(
        XElement element, WorkflowElementSnapshot snapshot)
    {
        // Null means "the studio did not send this", which must leave an existing
        // key alone -- a snapshot from an older SPA build clearing an author's
        // decision table would be indistinguishable from them removing it. Empty
        // string IS a clear, because that is what the picker sends for "none".
        if (snapshot.DecisionKey is null) return;

        element.SetAttributeValue(
            ScriptTaskIdentity.AutoNateNamespace + "decisionKey",
            string.IsNullOrWhiteSpace(snapshot.DecisionKey) ? null : snapshot.DecisionKey.Trim());
    }

    private static void ApplyScriptTaskSnapshot(XElement element, WorkflowElementSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ScriptFormat))
        {
            element.SetAttributeValue("scriptFormat", snapshot.ScriptFormat);
        }
        else
        {
            element.SetAttributeValue("scriptFormat", null);
        }

        // #416/#230: `flowable:resultVariable`, NOT a bare one. Flowable refuses a
        // bare `resultVariable` on a bpmn:scriptTask outright --
        // "cvc-complex-type.3.2.2: Attribute 'resultVariable' is not allowed to
        // appear in element 'scriptTask'" -- which the complex-gateway expansion
        // in this same file already knew, writing it namespaced with a comment
        // saying why.
        //
        // This was harmless until #411: the studio read the property as a direct
        // field, real bpmn-js routing puts an undeclared bare key in $attrs, so
        // the snapshot's ResultVariable was ALWAYS null and this branch never
        // fired. Fixing that read completed the chain and turned a latent defect
        // into a publish failure -- fixing one half of a two-half defect was worse
        // than fixing neither.
        //
        // The bare attribute is cleared too, so a diagram saved before this keeps
        // its value instead of carrying both spellings into the engine.
        element.SetAttributeValue("resultVariable", null);
        element.SetAttributeValue(
            FlowableNamespace + "resultVariable",
            string.IsNullOrWhiteSpace(snapshot.ResultVariable) ? null : snapshot.ResultVariable);

        var scriptElement = element.Element(BpmnNamespace + "script");
        if (snapshot.Script is null)
        {
            scriptElement?.Remove();
            return;
        }

        scriptElement ??= new XElement(BpmnNamespace + "script");
        scriptElement.Value = snapshot.Script;

        if (scriptElement.Parent is null)
        {
            element.Add(scriptElement);
        }
    }

    // #218. Reuses Script/ScriptFormat rather than adding snapshot fields: it is
    // the same concept in the same shape, and the studio routes on $type, so a
    // script task's snapshot and a gateway's cannot be confused.
    private static void ApplyComplexGatewaySnapshot(XElement element, WorkflowElementSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ScriptFormat))
        {
            element.SetAttributeValue(
                ScriptTaskIdentity.AutoNateNamespace + ComplexGatewayScriptFormatAttribute,
                snapshot.ScriptFormat);
        }

        // Null means the studio did not send one, which must not clear a script
        // someone already has — the same distinction RetryPoint draws.
        if (snapshot.Script is null) return;

        element.SetAttributeValue(
            ScriptTaskIdentity.AutoNateNamespace + ComplexGatewayScriptAttribute,
            string.IsNullOrWhiteSpace(snapshot.Script) ? null : snapshot.Script);

        // A diagram imported with the child form is normalised onto the
        // attribute, because the child will not survive the author's next save.
        element.Element(BpmnNamespace + "script")?.Remove();
    }

    private static void ApplyUserTaskSnapshot(XElement element, WorkflowElementSnapshot snapshot)
    {
        SetOrRemoveFlowableAttribute(element, "assignee", snapshot.Assignee);
        SetOrRemoveFlowableAttribute(element, "candidateUsers", SerializeFlowableList(snapshot.CandidateUsers));
        SetOrRemoveFlowableAttribute(element, "candidateGroups", SerializeFlowableList(snapshot.CandidateGroups));
        SetOrRemoveFlowableAttribute(element, "dueDate", snapshot.DueDate);
    }

    private static void SetOrRemoveFlowableAttribute(XElement element, string localName, string? value)
    {
        var attributeName = FlowableNamespace + localName;
        if (string.IsNullOrWhiteSpace(value))
        {
            element.SetAttributeValue(attributeName, null);
            return;
        }

        element.SetAttributeValue(attributeName, value);
    }

    private static string? SerializeFlowableList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var trimmed = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length == 1 && trimmed[0].StartsWith("${", StringComparison.Ordinal))
        {
            return trimmed[0];
        }

        return string.Join(",", trimmed);
    }

    // #159/#163/#166. An IMPORTED diagram declares no autonate namespace, and
    // without the declaration moddle cannot serialise an `autonate:` attribute at
    // all — the studio's panels appear to work, Apply reports success, and the
    // value is gone from the saved XML.
    //
    // Auton8's own starter diagram has always declared it, which is why this only
    // bites a diagram authored somewhere else. Declared on the prepare path, the
    // same place the flowable namespace is.
    private static void EnsureAutoNateNamespaceDeclared(XDocument document)
    {
        var definitions = document.Root;
        if (definitions is null) return;

        var alreadyDeclared = definitions.Attributes()
            .Any(a => a.IsNamespaceDeclaration
                      && a.Value == ScriptTaskIdentity.AutoNateNamespace.NamespaceName);
        if (alreadyDeclared) return;

        definitions.SetAttributeValue(
            XNamespace.Xmlns + "autonate", ScriptTaskIdentity.AutoNateNamespace.NamespaceName);
    }

    private static void EnsureFlowableNamespaceDeclared(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return;
        }

        if (root.GetNamespaceOfPrefix("flowable") is not null)
        {
            return;
        }

        root.SetAttributeValue(XNamespace.Xmlns + "flowable", FlowableNamespace.NamespaceName);
    }

    private static void ApplySequenceFlowSnapshot(XElement element, WorkflowElementSnapshot snapshot)
    {
        var conditionExpressionElement = element.Element(BpmnNamespace + "conditionExpression");
        if (string.IsNullOrWhiteSpace(snapshot.ConditionExpression))
        {
            conditionExpressionElement?.Remove();
            return;
        }

        conditionExpressionElement ??= new XElement(BpmnNamespace + "conditionExpression");

        // RESOLVED FROM `element`, NOT FROM THE CONDITION (#482). The condition
        // may have just been constructed and not yet added -- see the
        // `Parent is null` branch below -- and a detached element has no
        // namespace scope, so it would answer "unprefixed" for every document,
        // including the prefixed ones that work today. That would have turned a
        // fix into a regression.
        conditionExpressionElement.SetAttributeValue(
            XsiNamespace + "type", FormalExpressionType(element));
        conditionExpressionElement.Value = snapshot.ConditionExpression;

        if (conditionExpressionElement.Parent is null)
        {
            element.Add(conditionExpressionElement);
        }
    }

    private static string? ToBpmnLocalName(string? bpmnType)
    {
        if (string.IsNullOrWhiteSpace(bpmnType))
        {
            return null;
        }

        var separatorIndex = bpmnType.IndexOf(':', StringComparison.Ordinal);
        var localName = separatorIndex >= 0
            ? bpmnType[(separatorIndex + 1)..]
            : bpmnType;

        return localName.Length == 0
            ? localName
            : char.ToLowerInvariant(localName[0]) + localName[1..];
    }

    private static string BuildMissingProcessDefinitionMessage(XDocument document)
    {
        var root = document.Root;
        var rootName = root is null
            ? "<no-root>"
            : root.Name.NamespaceName.Length > 0
                ? $"{{{root.Name.NamespaceName}}}{root.Name.LocalName}"
                : root.Name.LocalName;

        var preview = document.ToString(SaveOptions.DisableFormatting);
        if (preview.Length > 300)
        {
            preview = preview[..300];
        }

        return $"The BPMN XML does not contain a process definition. Root element: {rootName}. Payload preview: {preview}";
    }

    private static IReadOnlyList<string> BuildSignalStartEventValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        var signalsById = document.Root?
            .Elements(BpmnNamespace + "signal")
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Attribute("id")?.Value))
            .ToDictionary(
                signal => signal.Attribute("id")!.Value,
                signal => signal,
                StringComparer.Ordinal)
            ?? new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var startEvent in document.Descendants(BpmnNamespace + "startEvent"))
        {
            var signalEventDefinition = startEvent.Element(BpmnNamespace + "signalEventDefinition");
            if (signalEventDefinition is null)
            {
                continue;
            }

            var label = startEvent.Attribute("name")?.Value
                ?? startEvent.Attribute("id")?.Value
                ?? "Unnamed signal start event";

            var signalRef = signalEventDefinition.Attribute("signalRef")?.Value;
            if (string.IsNullOrWhiteSpace(signalRef) || !signalsById.TryGetValue(signalRef, out var signal))
            {
                errors.Add($"Signal start event '{label}' must specify an Event Type before publishing.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(signal.Attribute("name")?.Value))
            {
                errors.Add($"Signal start event '{label}' must specify an Event Type before publishing.");
            }
        }

        return errors;
    }

    /// <summary>
    /// An event whose trigger is not named yet, at any position (#316).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the engine actually does</b>, measured against Flowable 8.0.0 at
    /// catch, start and boundary — the split is by trigger <em>type</em>, not by
    /// position, and every cell below was deployed rather than reasoned about:
    /// </para>
    /// <para>
    /// <code>
    ///                       signal            message
    ///   ref -> named root   deploys           deploys
    ///   ref -> UNNAMED root REFUSED           deploys
    ///   ref absent          REFUSED           REFUSED
    ///   ref -> missing root REFUSED           REFUSED
    /// </code>
    /// </para>
    /// <para>
    /// So a signal must be <em>named</em>; a message need only <em>resolve</em>.
    /// The first version of this rule required a name for both, which made it a
    /// false refusal on every message event — Auton8 was stricter than the engine
    /// with no declared departure, in a milestone whose whole method is that such
    /// departures live in one place (#335).
    /// </para>
    /// <para>
    /// That mistake had a specific cause worth keeping written down: the signal
    /// half was verified against a live engine and the message half was assumed to
    /// match. It did not. Nothing here is generalised across the two columns any
    /// more — the table above is the rule.
    /// </para>
    /// <para>
    /// An unnamed message still cannot be <em>correlated</em>, because correlation
    /// matches on name — so it deploys and then waits forever. That is a warning,
    /// not an error: the engine accepts it, and the studio currently offers no way
    /// to name a message root at all (the Message field is disabled for everything
    /// but a Send Task, and nothing in the SPA emits a <c>bpmn:message</c>). An
    /// error whose remedy the product does not offer is worse than none. Making it
    /// nameable is #328, and the silent-no-op oracle that should own this class of
    /// defect is #325.
    /// </para>
    /// <para>
    /// **That is the state the palette produces.** Place a signal catch, a message
    /// boundary, a signal end — anything but a start event — and before the author
    /// opens the panel and types a name, the diagram is undeployable. Publish said
    /// nothing about it.
    /// </para>
    /// <para>
    /// A rule existed for <b>signal start</b> only, which is the tell: it was
    /// written for the position someone happened to test, and its four siblings —
    /// catch, throw, boundary, end — had none, and there was no message rule at any
    /// position. Rather than add the missing seven by hand, this walks every
    /// position, because the next position added to the product is then covered by
    /// construction.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> a placement rule: where the element may sit is
    /// <c>BuildStartEventPlacementErrors</c>'s question. This one is about
    /// configuration state, which is a different axis and — per #324 — one the
    /// manifest has no column for either.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
        BuildUnnamedEventTriggerFindings(XDocument document)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // definition local name -> (root element, ref attribute, author's noun,
        // and whether the ENGINE requires the root to carry a name).
        // NameRequired is not a style choice: it is the measured column in the
        // table above. Signals refuse unnamed; messages deploy unnamed (#335).
        var triggers = new (string Definition, string Root, string RefAttribute, string Noun, bool NameRequired)[]
        {
            ("signalEventDefinition", "signal", "signalRef", "signal", true),
            ("messageEventDefinition", "message", "messageRef", "message", false),
        };

        foreach (var (definitionName, rootName, refAttribute, noun, nameRequired) in triggers)
        {
            var rootsById = document.Descendants(BpmnNamespace + rootName)
                .Where(root => !string.IsNullOrWhiteSpace(root.Attribute("id")?.Value))
                .ToDictionary(root => root.Attribute("id")!.Value, root => root, StringComparer.Ordinal);

            foreach (var definition in document.Descendants(BpmnNamespace + definitionName))
            {
                var owner = definition.Parent;
                if (owner is null || owner.Name.Namespace != BpmnNamespace) continue;

                var reference = definition.Attribute(refAttribute)?.Value;
                var resolves = !string.IsNullOrWhiteSpace(reference)
                               && rootsById.TryGetValue(reference!, out _);

                // Unresolvable is refused by the engine for BOTH triggers, at every
                // position. This half of the rule was always right.
                if (!resolves)
                {
                    errors.Add(
                        $"{DescribeEventPosition(owner)} '{LabelOf(owner)}' has no {noun} set yet. " +
                        $"Open it and choose the {noun} it should use — the {noun} is what matches " +
                        "one end to the other. Left unset, Flowable refuses the whole deployment, " +
                        "not just this step.");
                    continue;
                }

                var named = !string.IsNullOrWhiteSpace(
                    rootsById[reference!].Attribute("name")?.Value);

                if (named) continue;

                if (nameRequired)
                {
                    errors.Add(
                        $"{DescribeEventPosition(owner)} '{LabelOf(owner)}' points at a {noun} with " +
                        $"no name. Give the {noun} a name — it is what matches one end to the other. " +
                        "Left blank, Flowable refuses the whole deployment, not just this step.");
                }
                else
                {
                    // Deploys, then waits forever, because correlation matches on
                    // name. Not an error: the engine accepts it and the studio has
                    // no way to name a message root yet (#328).
                    warnings.Add(
                        $"{DescribeEventPosition(owner)} '{LabelOf(owner)}' points at a {noun} with " +
                        $"no name. It will deploy, but nothing can ever match it, so this step will " +
                        "wait forever.");
                }
            }
        }

        // An unnamed <bpmn:signal> ROOT sinks the whole deployment on its own,
        // whether or not anything references it -- measured, orphan root in an
        // otherwise valid diagram: signal REFUSED, message DEPLOYED (#335).
        //
        // This is separate from the per-event loop above because the defect is in
        // the root, not the event: a diagram with no signal events at all still
        // fails if it carries one. `PruneOrphanSignalRoots` removes these during
        // prepare, but publish validates the STORED xml and a caller may publish
        // without preparing, which is exactly the path the endpoint's own comment
        // says must not get through.
        foreach (var root in document.Descendants(BpmnNamespace + "signal"))
        {
            if (!string.IsNullOrWhiteSpace(root.Attribute("name")?.Value)) continue;

            var id = root.Attribute("id")?.Value;
            errors.Add(
                $"This workflow declares a signal with no name{(string.IsNullOrWhiteSpace(id) ? "" : $" ('{id}')")}. " +
                "Flowable refuses the whole deployment over it even when nothing uses it. " +
                "Name it, or remove it.");
        }

        return (errors, warnings);
    }

    /// <summary>
    /// Elements the engine's validator refuses for a missing required attribute (#333).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The pattern, stated once because it keeps recurring:</b> if Flowable has a
    /// "missing required attribute" validation for an element the studio can place,
    /// publish needs the matching refusal. Without it the element draws, publishes
    /// and then either sinks the deployment with a Java parser dump or — worse —
    /// deploys and does nothing.
    /// </para>
    /// <para>
    /// Measured against Flowable 8.0.0, each in the state the palette actually
    /// leaves it:
    /// </para>
    /// <para>
    /// <code>
    ///   serviceTask, no implementation  REFUSED  flowable-servicetask-missing-implementation
    ///   multiInstance, no collection    REFUSED  flowable-multi-instance-missing-collection
    ///   callActivity, no target         DEPLOYS  -- then every start fails 400:
    ///                                            "Process definition null was not found"
    /// </code>
    /// </para>
    /// <para>
    /// The call activity is the worst of the three and is the founding complaint
    /// verbatim: it draws fine, publishes, deploys, and does nothing.
    /// <c>ExtractCallActivityTargets</c> skips an empty key, so the publish
    /// endpoint's own comment — "a key resolving to nothing is refused here rather
    /// than deployed" — did not describe the as-placed state.
    /// </para>
    /// <para>
    /// All three are reachable without hand-editing XML: <c>create.service-task</c>
    /// sets no properties, <c>toggle-parallel-mi</c> is a header entry the
    /// manifest-derived filter keeps because the rows are supported, and
    /// <c>create.call-activity</c> places a bare one.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildMissingRequiredAttributeErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var task in document.Descendants(BpmnNamespace + "serviceTask"))
        {
            // Flowable's four implementation attributes, and ONLY those.
            //
            // `behaviorKey` was in this list and is not an implementation: it is
            // an Auton8 attribute the expansion reads, and Flowable has never
            // heard of it. Measured -- a service task carrying only
            // flowable:behaviorKey is REFUSED with
            // flowable-servicetask-missing-implementation, while the same task
            // with delegateExpression alongside DEPLOYS. So accepting it alone
            // let an imported diagram publish clean and sink the deployment,
            // which is the exact class this rule exists to close (#338).
            //
            // The expansion writes delegateExpression onto behaviour tasks
            // (WorkflowBpmnXml.cs:225/2012), so a prepared diagram always carries
            // one; nothing the studio produces relies on behaviorKey alone.
            var wired =
                !string.IsNullOrWhiteSpace(task.Attribute(FlowableNamespace + "delegateExpression")?.Value)
                || !string.IsNullOrWhiteSpace(task.Attribute(FlowableNamespace + "class")?.Value)
                || !string.IsNullOrWhiteSpace(task.Attribute(FlowableNamespace + "expression")?.Value)
                || !string.IsNullOrWhiteSpace(task.Attribute(FlowableNamespace + "type")?.Value);

            if (wired) continue;

            errors.Add(
                $"Service task '{LabelOf(task)}' has no behaviour chosen yet. Open it and pick what " +
                "it should do. Left unset, Flowable refuses the whole deployment, not just this step.");
        }

        foreach (var loop in document.Descendants(BpmnNamespace + "multiInstanceLoopCharacteristics"))
        {
            // Flowable takes EITHER a collection to iterate or a fixed cardinality,
            // and both have two spellings. Through the shared readers, always --
            // this rule read only the child element and so refused every
            // fixed-count multi-instance the studio could produce (#356).
            if (DeclaresCollection(loop) || DeclaresCardinality(loop)) continue;

            var owner = loop.Parent;
            var label = owner is null ? "this step" : $"'{LabelOf(owner)}'";
            errors.Add(
                $"The repeat on {label} has nothing to repeat over. Set the collection it should " +
                "run once per item of, or a fixed number of times. Left unset, Flowable refuses " +
                "the whole deployment, not just this step.");
        }

        foreach (var call in document.Descendants(BpmnNamespace + "callActivity"))
        {
            if (!string.IsNullOrWhiteSpace(call.Attribute("calledElement")?.Value)) continue;

            // This one DEPLOYS. That is why it needs refusing here rather than
            // being left to the engine: there is no deployment error to surface,
            // only an instance that fails the moment a token reaches the call.
            errors.Add(
                $"Call activity '{LabelOf(call)}' does not say which workflow to call. Open it and " +
                "choose one. Left unset this publishes and deploys, and then every run fails the " +
                "moment it reaches this step — Flowable reports \"Process definition null was not found\".");
        }

        return errors;
    }

    /// <summary>
    /// A send task Auton8 cannot turn into something deployable (#316).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable requires <c>type</c> or <c>operation</c> on a <c>sendTask</c> and
    /// refuses the <b>whole deployment</b> otherwise
    /// (<c>flowable-sendtask-invalid-implementation</c>). Auton8's expansion
    /// converts one to a service task when it carries
    /// <c>flowable:behaviorKey="autonate.send-message"</c>, and otherwise leaves it
    /// exactly as authored.
    /// </para>
    /// <para>
    /// **#316: the studio could not write that key.** `updateServiceTaskProperties`
    /// throws unless the element is a `bpmn:ServiceTask`, and selecting a send task
    /// routes to the message editor, whose only write was the message name. So every
    /// send task an author could place was undeployable, publish said nothing, and
    /// the element's manifest row said `studio: supported` / `engine: executes`.
    /// The row was withdrawn.
    /// </para>
    /// <para>
    /// **#328 made it authorable again**: the message editor now writes the key
    /// beside the message name, so a send task the studio produces carries one of
    /// the three deployable wirings and the row is `studio: supported` with a test
    /// on each side of the contract. This rule keeps its job — an IMPORTED diagram
    /// can still carry a send task with none of the three, and it is refused here
    /// rather than deployed into a whole-deployment failure.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildSendTaskErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var sendTask in document.Descendants(BpmnNamespace + "sendTask"))
        {
            // Any of the three makes it deployable: Flowable's own two wirings, or
            // ours, which the expansion turns into a service task.
            var deployable =
                !string.IsNullOrWhiteSpace(sendTask.Attribute(FlowableNamespace + "type")?.Value)
                || !string.IsNullOrWhiteSpace(sendTask.Attribute(FlowableNamespace + "operation")?.Value)
                || string.Equals(
                    sendTask.Attribute(FlowableNamespace + "behaviorKey")?.Value,
                    SendMessageBehaviorKey,
                    StringComparison.Ordinal);

            if (deployable) continue;

            errors.Add(
                $"Send task '{LabelOf(sendTask)}' has nothing to send with. Flowable needs a " +
                "send task to name how it sends. Open it in the studio and give it a message " +
                "name — that writes the wiring Auton8 needs. If this diagram came from " +
                "elsewhere, give it a flowable:type or flowable:operation of its own, or " +
                "replace it with a service task or an intermediate throw message event. Left " +
                "as it is, Flowable refuses the whole deployment.");
        }

        return errors;
    }

    /// <summary>How an author would refer to the element carrying a definition.</summary>
    private static string DescribeEventPosition(XElement owner) => owner.Name.LocalName switch
    {
        "startEvent" => "Start event",
        "endEvent" => "End event",
        "boundaryEvent" => "Boundary event",
        "intermediateCatchEvent" => "Catch event",
        "intermediateThrowEvent" => "Throw event",
        "receiveTask" => "Receive task",
        "sendTask" => "Send task",
        _ => owner.Name.LocalName,
    };

    private static IReadOnlyList<string> BuildRecordTypeFilterMisplacementErrors(XDocument document)
    {
        var errors = new List<string>();
        foreach (var signalEventDef in document.Descendants(BpmnNamespace + "signalEventDefinition"))
        {
            if (signalEventDef.Attribute(FlowableNamespace + "recordTypeShortCodes") is null)
            {
                continue;
            }

            var parent = signalEventDef.Parent;
            if (parent is null)
            {
                continue;
            }

            if (parent.Name != BpmnNamespace + "startEvent")
            {
                var elementId = parent.Attribute("id")?.Value ?? "(unknown)";
                errors.Add(
                    $"Element '{elementId}': flowable:recordTypeShortCodes is only supported on signal startEvent (found on {parent.Name.LocalName}).");
            }
        }
        return errors;
    }

    private static IReadOnlyList<string> BuildTimerStartEventValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var startEvent in document.Descendants(BpmnNamespace + "startEvent"))
        {
            var timerEventDefinition = startEvent.Element(BpmnNamespace + "timerEventDefinition");
            if (timerEventDefinition is null)
            {
                continue;
            }

            var label = startEvent.Attribute("name")?.Value
                ?? startEvent.Attribute("id")?.Value
                ?? "Unnamed timer start event";

            // Reject co-existence with other event definitions on the same start
            // event — Flowable will deploy it but the resulting trigger
            // semantics are ambiguous, and we don't want to surprise users.
            var conflictingDefinitions = startEvent.Elements()
                .Where(child => child.Name.Namespace == BpmnNamespace)
                .Select(child => child.Name.LocalName)
                .Where(name =>
                    name.Equals("signalEventDefinition", StringComparison.Ordinal) ||
                    name.Equals("messageEventDefinition", StringComparison.Ordinal) ||
                    name.Equals("conditionalEventDefinition", StringComparison.Ordinal) ||
                    name.Equals("errorEventDefinition", StringComparison.Ordinal) ||
                    name.Equals("escalationEventDefinition", StringComparison.Ordinal) ||
                    name.Equals("compensateEventDefinition", StringComparison.Ordinal))
                .ToArray();
            if (conflictingDefinitions.Length > 0)
            {
                errors.Add(
                    $"Timer start event '{label}' cannot also have a {string.Join(", ", conflictingDefinitions)} — drop a fresh start event for the other trigger type.");
                continue;
            }

            var timerKindChildren = timerEventDefinition.Elements()
                .Where(child => child.Name.Namespace == BpmnNamespace &&
                    (child.Name.LocalName == "timeCycle" ||
                     child.Name.LocalName == "timeDate" ||
                     child.Name.LocalName == "timeDuration"))
                .ToArray();
            if (timerKindChildren.Length == 0)
            {
                errors.Add($"Timer start event '{label}' must specify a recurrence schedule before publishing.");
                continue;
            }
            if (timerKindChildren.Length > 1)
            {
                errors.Add($"Timer start event '{label}' may only specify one of timeCycle, timeDate, or timeDuration.");
                continue;
            }

            var timerKind = timerKindChildren[0];
            var body = timerKind.Value?.Trim();
            if (string.IsNullOrEmpty(body))
            {
                errors.Add($"Timer start event '{label}' has an empty schedule expression.");
                continue;
            }

            if (timerKind.Name.LocalName == "timeCycle")
            {
                var typeAttribute = timerKind.Attribute(FlowableNamespace + "type")?.Value;
                if (string.Equals(typeAttribute, "cron", StringComparison.OrdinalIgnoreCase) &&
                    !LooksLikeQuartzCron(body))
                {
                    errors.Add($"Timer start event '{label}' has an invalid cron expression: '{body}'.");
                    continue;
                }
            }

            // endDate may live either as an attribute (the round-trip-safe
            // shape we emit) or as a child element (older XML or
            // hand-edited workflows). Accept both for validation.
            var endDateValue = timerEventDefinition.Attribute(FlowableNamespace + "endDate")?.Value?.Trim()
                ?? timerEventDefinition.Element(FlowableNamespace + "endDate")?.Value?.Trim();
            if (!string.IsNullOrEmpty(endDateValue) && !LooksLikeIsoDateOrDateTime(endDateValue))
            {
                errors.Add($"Timer start event '{label}' has an invalid end date '{endDateValue}'. Use YYYY-MM-DD or YYYY-MM-DDTHH:mm:ss.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> BuildServiceTaskValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var serviceTask in document.Descendants(BpmnNamespace + "serviceTask"))
        {
            var label = serviceTask.Attribute("name")?.Value
                ?? serviceTask.Attribute("id")?.Value
                ?? "Unnamed service task";

            // Resolve the AutoNate-managed wiring. We accept hand-written XML
            // that points at a different delegate (e.g. a custom Java class
            // shipped via a future plugin) and skip behavior validation for
            // those — only configurations the studio creates need a behavior
            // key.
            var delegateExpression = serviceTask.Attribute(FlowableNamespace + "delegateExpression")?.Value;
            if (!string.Equals(delegateExpression, AutoNateBehaviorDelegateExpression, StringComparison.Ordinal))
            {
                continue;
            }

            var (kind, behaviorKey) = ReadServiceTaskBehaviorConfig(serviceTask);

            if (!string.Equals(kind, ServiceTaskBehaviorKind, StringComparison.Ordinal))
            {
                errors.Add($"Service task '{label}' has unsupported autonateServiceKind '{kind}'. Only 'behavior' is supported.");
                continue;
            }

            if (string.IsNullOrEmpty(behaviorKey))
            {
                errors.Add($"Service task '{label}' must have a behavior selected before publishing.");
            }
        }

        return errors;
    }

    // Reads (kind, behaviorKey) from a serviceTask element. Prefers
    // flowable: attributes (current shape); falls back to the legacy
    // <flowable:field>-injection shape produced by an older iteration so
    // workflows saved with that build still validate correctly.
    private static (string Kind, string? BehaviorKey) ReadServiceTaskBehaviorConfig(XElement serviceTask)
    {
        var kindAttr = serviceTask.Attribute(FlowableNamespace + "autonateServiceKind")?.Value?.Trim();
        var keyAttr = serviceTask.Attribute(FlowableNamespace + "behaviorKey")?.Value?.Trim();

        if (!string.IsNullOrEmpty(kindAttr) || !string.IsNullOrEmpty(keyAttr))
        {
            return (string.IsNullOrEmpty(kindAttr) ? ServiceTaskBehaviorKind : kindAttr, keyAttr);
        }

        var fields = serviceTask
            .Element(BpmnNamespace + "extensionElements")
            ?.Elements(FlowableNamespace + "field")
            .ToDictionary(
                field => field.Attribute("name")?.Value ?? string.Empty,
                field => field.Element(FlowableNamespace + "string")?.Value?.Trim() ?? string.Empty,
                StringComparer.Ordinal);

        var legacyKind = fields?.TryGetValue("autonateServiceKind", out var k) == true ? k : ServiceTaskBehaviorKind;
        var legacyKey = fields?.TryGetValue("behaviorKey", out var b) == true ? b : null;
        return (legacyKind, legacyKey);
    }

    private static IReadOnlyList<string> BuildTimerIntermediateCatchEventValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var catchEvent in document.Descendants(BpmnNamespace + "intermediateCatchEvent"))
        {
            var timerEventDefinition = catchEvent.Element(BpmnNamespace + "timerEventDefinition");
            if (timerEventDefinition is null)
            {
                continue;
            }

            var label = catchEvent.Attribute("name")?.Value
                ?? catchEvent.Attribute("id")?.Value
                ?? "Unnamed timer intermediate catch event";

            var timerKindChildren = timerEventDefinition.Elements()
                .Where(child => child.Name.Namespace == BpmnNamespace &&
                    (child.Name.LocalName == "timeDuration" ||
                     child.Name.LocalName == "timeDate" ||
                     child.Name.LocalName == "timeCycle"))
                .ToArray();
            if (timerKindChildren.Length == 0)
            {
                errors.Add($"Timer intermediate catch event '{label}' must specify a duration or date before publishing.");
                continue;
            }
            if (timerKindChildren.Length > 1)
            {
                errors.Add($"Timer intermediate catch event '{label}' may only specify one of timeDuration or timeDate.");
                continue;
            }

            var timerKind = timerKindChildren[0];
            var body = timerKind.Value?.Trim();
            if (string.IsNullOrEmpty(body))
            {
                errors.Add($"Timer intermediate catch event '{label}' has an empty timer expression.");
                continue;
            }

            // Cycle isn't a documented mode in this UI; if a hand-edited file
            // uses it, surface that rather than silently ignoring.
            if (timerKind.Name.LocalName == "timeCycle")
            {
                errors.Add($"Timer intermediate catch event '{label}' uses timeCycle — only timeDuration or timeDate are supported here.");
                continue;
            }

            // Expressions are evaluated by Flowable at event entry; we can't
            // syntax-check them here, so only validate hard-coded literals.
            if (LooksLikeFlowableExpression(body))
            {
                continue;
            }

            if (timerKind.Name.LocalName == "timeDuration" && !LooksLikeIso8601Duration(body))
            {
                errors.Add($"Timer intermediate catch event '{label}' has an invalid duration '{body}'. Use ISO 8601 like PT15M or P1DT2H, or a Flowable expression.");
                continue;
            }

            if (timerKind.Name.LocalName == "timeDate" && !LooksLikeIsoDateOrDateTime(body))
            {
                errors.Add($"Timer intermediate catch event '{label}' has an invalid date '{body}'. Use YYYY-MM-DD or YYYY-MM-DDTHH:mm:ss, or a Flowable expression.");
            }
        }

        return errors;
    }

    private static bool LooksLikeFlowableExpression(string value)
    {
        return value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}');
    }

    private static bool LooksLikeIso8601Duration(string value)
    {
        return Iso8601DurationRegex().IsMatch(value);
    }

    [GeneratedRegex(@"^P(?!$)(\d+Y)?(\d+M)?(\d+W)?(\d+D)?(T(?=\d)(\d+H)?(\d+M)?(\d+S)?)?$", RegexOptions.Compiled)]
    private static partial Regex Iso8601DurationRegex();

    private static bool LooksLikeQuartzCron(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (6 or 7))
        {
            return false;
        }

        // Quartz allows ?, *, ,, -, /, L, W, # plus literal day/month names.
        // The field-by-field grammar is rich; this regex only weeds out
        // obvious nonsense (control chars, unknown letters). The Flowable
        // engine will surface a precise error on deployment if the expression
        // is technically syntactically valid but semantically wrong.
        return QuartzCronRegex().IsMatch(expression);
    }

    [GeneratedRegex(@"^[0-9A-Z\?\*\,\-\/#LW\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex QuartzCronRegex();

    private static bool LooksLikeIsoDateOrDateTime(string value)
    {
        return DateTime.TryParseExact(
                value,
                ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.fff"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out _);
    }

    private static IReadOnlyList<string> BuildScriptTaskValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var scriptTask in document.Descendants(BpmnNamespace + "scriptTask"))
        {
            var taskLabel = scriptTask.Attribute("name")?.Value
                ?? scriptTask.Attribute("id")?.Value
                ?? "Unnamed script task";
            var scriptFormat = scriptTask.Attribute("scriptFormat")?.Value;
            if (!ScriptSurfaceRules.IsSupportedScriptFormat(scriptFormat))
            {
                // A list since #154. Both languages run in the same sandbox
                // against the same host surface, so this is a front-end choice
                // rather than a second execution path.
                var supported = string.Join(
                    " or ",
                    ScriptSurfaceRules.SupportedScriptFormats.Select(f => $"\"{f}\""));
                errors.Add($"Script task '{taskLabel}' must use scriptFormat={supported}.");
            }

            var scriptBody = scriptTask.Element(BpmnNamespace + "script")?.Value;
            if (string.IsNullOrWhiteSpace(scriptBody))
            {
                errors.Add($"Script task '{taskLabel}' must include a non-empty inline script body.");
            }

            // #151: scripts run in the executor sandbox since #147, so the old
            // `execution` binding and any reach for the JVM now fail at
            // runtime — on whoever happens to run the process, with an error
            // that does not say how to fix it. Catch them here instead, while
            // the author still has the editor open, and name the replacement.
            foreach (var rejection in ScriptSurfaceRules.FindRejected(scriptBody))
            {
                errors.Add($"Script task '{taskLabel}': {rejection}");
            }
        }

        errors.AddRange(BuildComplexGatewayValidationErrors(document));
        return errors;
    }

    // #218. A complex gateway's routing script becomes a real script task at
    // publish, so it is held to the same rules as one the author drew.
    //
    // Validated on the AUTHORED document rather than the expanded one, because
    // the author has to be told which gateway is wrong — after expansion the
    // offending element is a generated node whose id means nothing to them.
    private static IReadOnlyList<string> BuildComplexGatewayValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        var flowsBySource = document
            .Descendants(BpmnNamespace + "sequenceFlow")
            .GroupBy(flow => flow.Attribute("sourceRef")?.Value ?? string.Empty)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var gateway in document.Descendants(BpmnNamespace + "complexGateway"))
        {
            var gatewayId = gateway.Attribute("id")?.Value;
            var label = gateway.Attribute("name")?.Value ?? gatewayId ?? "Unnamed complex gateway";

            var scriptFormat = ReadComplexGatewayScriptFormat(gateway);
            // Unset is fine — the expansion defaults it to javascript. Set to
            // something the sandbox cannot run is not.
            if (!string.IsNullOrWhiteSpace(scriptFormat)
                && !ScriptSurfaceRules.IsSupportedScriptFormat(scriptFormat))
            {
                var supported = string.Join(
                    " or ",
                    ScriptSurfaceRules.SupportedScriptFormats.Select(f => $"\"{f}\""));
                errors.Add($"Complex gateway '{label}' must use scriptFormat={supported}.");
            }

            // The same sandbox, so the same surface rules. Skipping this would
            // leave one script in the product that can still reach for the JVM
            // binding, found at run time by whoever starts the process.
            var scriptBody = ReadComplexGatewayScript(gateway);
            if (!string.IsNullOrWhiteSpace(scriptBody))
            {
                foreach (var rejection in ScriptSurfaceRules.FindRejected(scriptBody))
                {
                    errors.Add($"Complex gateway '{label}': {rejection}");
                }
            }

            if (string.IsNullOrWhiteSpace(gatewayId)) continue;

            var outgoing = flowsBySource.TryGetValue(gatewayId, out var flows) ? flows : [];
            var defaultFlowId = Trimmed(gateway.Attribute("default")?.Value);
            var routeCount = outgoing.Count(flow =>
                !string.IsNullOrWhiteSpace(flow.Attribute("id")?.Value)
                && flow.Attribute("id")!.Value != defaultFlowId);

            // #239. A route the script may return whose flow carries the author's
            // own condition is a silent misroute waiting to happen: the contract
            // accepts 'fa', the author's ${1 == 2} is false, and the engine takes
            // the default. No exception, no dead letter — the exact failure this
            // milestone exists to end.
            //
            // The expansion deliberately leaves an author-written condition alone,
            // so the two sets have to be reconciled HERE rather than silently
            // diverging.
            foreach (var flow in outgoing)
            {
                var flowId = Trimmed(flow.Attribute("id")?.Value);
                if (flowId is null || flowId == defaultFlowId) continue;
                if (flow.Element(BpmnNamespace + "conditionExpression") is null) continue;

                var flowLabel = Trimmed(flow.Attribute("name")?.Value) ?? flowId;
                errors.Add(
                    $"The route '{flowLabel}' out of the complex gateway '{label}' has its own " +
                    "condition. The gateway's script chooses the route, so a condition here can " +
                    "send the process somewhere the script did not choose and nothing would report " +
                    "it. Remove the condition, or use an exclusive gateway instead of a complex one.");
            }

            if (routeCount == 0)
            {
                // Flowable deploys this happily and the instance then fails at
                // the gateway with an engine-level message. Refusing it here
                // names the gateway while the author still has it open.
                errors.Add(
                    $"Complex gateway '{label}' needs at least one outgoing route for its script to choose. " +
                    "A gateway with only a default flow has nothing to route.");
            }
        }

        return errors;
    }

    // #107: elements the manifest marks unsupported are a DEPLOYMENT ERROR, not a
    // warning.
    //
    // `BuildUnsupportedRuntimeWarnings` fed `warnings`, so an element the engine
    // cannot run deployed cleanly and then did nothing — the founding complaint of
    // #40, and the current default rather than a theoretical risk. #103 confirmed
    // it mechanically: Complex Gateway deploys, an instance starts, and the token
    // passes straight through with no activation condition evaluated.
    //
    // Keyed by variant through the manifest, so this reports the one boundary
    // event that cannot run rather than all eight. Note it keys on the manifest's
    // ENGINE axis, not its studio axis: an element Flowable runs deploys even while
    // the studio still lists it as coming soon, because "we have not built the
    // property editor yet" is not a reason to reject a hand-authored diagram.
    // ── #169: collaborations ──────────────────────────────────────────────────
    //
    // BPMN says each pool is its own process. Auton8 says a diagram is one
    // authored unit. Both hold at once by deploying N definitions as ONE Flowable
    // deployment (one multipart file already is one), and by choosing one pool --
    // the primary -- to carry the workflow key and to be what "start" means.

    /// <summary>
    /// Element local names that make a process something the engine can run. A
    /// process with none of these is a drawn-only counterparty.
    /// </summary>
    private static bool IsFlowNode(XElement element)
    {
        if (element.Name.Namespace != BpmnNamespace) return false;
        var local = element.Name.LocalName;
        return local.EndsWith("Event", StringComparison.Ordinal)
            || local.EndsWith("Task", StringComparison.Ordinal)
            || local.EndsWith("Gateway", StringComparison.Ordinal)
            || local is "task" or "subProcess" or "callActivity" or "transaction" or "adHocSubProcess";
    }

    private static bool HasFlowNodes(XElement process) => process.Elements().Any(IsFlowNode);

    /// <summary>
    /// The process the workflow key names and the one <c>start</c> starts (#169).
    /// </summary>
    /// <remarks>
    /// With a collaboration: the first participant, in collaboration order, whose
    /// process contains a flow node; failing that, the first participant whose
    /// process resolves. Without one: the first <c>&lt;process&gt;</c>, exactly as
    /// before, so single-pool and no-pool diagrams are untouched by this story.
    /// </remarks>
    private static XElement? ResolvePrimaryProcess(XDocument document)
    {
        var processesById = document.Descendants(BpmnNamespace + "process")
            .Where(p => !string.IsNullOrWhiteSpace(p.Attribute("id")?.Value))
            .GroupBy(p => p.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var participants = document.Descendants(BpmnNamespace + "participant").ToList();
        if (participants.Count > 0)
        {
            XElement? firstResolving = null;
            foreach (var participant in participants)
            {
                var processRef = participant.Attribute("processRef")?.Value;
                if (processRef is null || !processesById.TryGetValue(processRef, out var process)) continue;
                firstResolving ??= process;
                if (HasFlowNodes(process)) return process;
            }

            if (firstResolving is not null) return firstResolving;
        }

        return document.Descendants(BpmnNamespace + "process").FirstOrDefault();
    }

    /// <summary>
    /// After the primary process is renamed: point its participant at the new id,
    /// name every other pool's process after its participant, and mark a pool with
    /// nothing in it non-executable (#169).
    /// </summary>
    private static void ApplyCollaborationMetadata(
        XDocument document,
        XElement primaryProcess,
        string? oldPrimaryId,
        string newPrimaryId)
    {
        var participants = document.Descendants(BpmnNamespace + "participant").ToList();
        if (participants.Count == 0) return;

        var processesById = document.Descendants(BpmnNamespace + "process")
            .Where(p => !string.IsNullOrWhiteSpace(p.Attribute("id")?.Value))
            .GroupBy(p => p.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var participant in participants)
        {
            var processRef = participant.Attribute("processRef")?.Value;
            if (processRef is null) continue;

            if (oldPrimaryId is not null && processRef == oldPrimaryId)
            {
                // The half that was missing: the reference follows the rename.
                participant.SetAttributeValue("processRef", newPrimaryId);
                continue;
            }

            if (!processesById.TryGetValue(processRef, out var process) || ReferenceEquals(process, primaryProcess))
            {
                continue;
            }

            var participantName = participant.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(participantName))
            {
                process.SetAttributeValue("name", participantName);
            }

            // A counterparty drawn for context deploys as nothing. Said explicitly
            // in the XML rather than left to whatever the modeller wrote, so the
            // engine, the prepare response and the studio all agree on it.
            process.SetAttributeValue("isExecutable", HasFlowNodes(process) ? "true" : "false");
        }

        // #170. After the renames, so a target in the primary pool resolves to
        // the workflow key rather than the id it had a moment ago.
        ApplyMessageFlows(document);
    }

    // ── #170: message flows ─────────────────────────────────────────────────
    //
    // The engine never executes a message flow. What executes is the SEND at its
    // source -- a send task or a message throw/end event, which #112 runs through
    // SendMessageBehavior -- addressed by the three `autonate*` attributes that
    // behaviour reads off the sender's stored diagram. A message flow is
    // therefore compiled at prepare into exactly those attributes: the flow the
    // author drew becomes the addressing the author used to type by hand.

    private static readonly HashSet<string> MessageFlowSourceKinds =
        new(StringComparer.Ordinal) { "sendTask", "intermediateThrowEvent", "endEvent" };

    private static readonly HashSet<string> MessageFlowTargetKinds =
        new(StringComparer.Ordinal) { "receiveTask", "intermediateCatchEvent", "boundaryEvent", "startEvent" };

    private static bool CarriesMessageDefinition(XElement element) =>
        element.Elements(BpmnNamespace + "messageEventDefinition").Any();

    private static bool IsValidMessageFlowSource(XElement element) =>
        element.Name.Namespace == BpmnNamespace
        && (element.Name.LocalName == "sendTask"
            || (MessageFlowSourceKinds.Contains(element.Name.LocalName) && CarriesMessageDefinition(element)));

    private static bool IsValidMessageFlowTarget(XElement element) =>
        element.Name.Namespace == BpmnNamespace
        && (element.Name.LocalName == "receiveTask"
            || (MessageFlowTargetKinds.Contains(element.Name.LocalName) && CarriesMessageDefinition(element))
            || (element.Name.LocalName == "participant" && ParticipantMessageStart(element) is not null));

    /// <summary>
    /// A flow drawn to a POOL rather than to an element in it -- which BPMN allows
    /// and the studio draws for a collapsed pool -- delivers to the pool's message
    /// start event, if it has exactly one (#170). Null for an empty pool, a pool
    /// with no message start, or one with several (ambiguous).
    /// </summary>
    private static XElement? ParticipantMessageStart(XElement participant)
    {
        var processRef = participant.Attribute("processRef")?.Value;
        if (string.IsNullOrWhiteSpace(processRef)) return null;
        var process = participant.Document?.Descendants(BpmnNamespace + "process")
            .FirstOrDefault(p => p.Attribute("id")?.Value == processRef);
        var starts = process?.Elements(BpmnNamespace + "startEvent").Where(CarriesMessageDefinition).ToList();
        return starts is { Count: 1 } ? starts[0] : null;
    }

    private static XElement? ProcessOf(XElement element) =>
        element.Ancestors(BpmnNamespace + "process").FirstOrDefault();

    private static Dictionary<string, XElement> ElementsById(XDocument document) =>
        document.Descendants()
            .Where(e => e.Name.Namespace == BpmnNamespace && !string.IsNullOrWhiteSpace(e.Attribute("id")?.Value))
            .GroupBy(e => e.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>
    /// Stamp each message flow's target onto its source (#170), so the flow the
    /// author drew is the addressing <c>SendMessageBehavior</c> reads. Runs after
    /// the primary process has been renamed, so a target in the primary pool
    /// resolves to the workflow key.
    /// </summary>
    /// <summary>
    /// Where a message flow drawn FROM this element goes (#170): the target
    /// pool's process id, the name the target answers to, and the target's
    /// declared correlation key. Null when no valid flow leaves the element.
    /// </summary>
    private static (string ProcessId, string MessageName, string? MessageRef, string? CorrelationKey)? ResolveMessageFlowTarget(
        XDocument document,
        XElement source,
        IReadOnlyDictionary<string, string> messageNamesById)
    {
        var sourceId = source.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceId) || !IsValidMessageFlowSource(source)) return null;

        var flow = document.Descendants(BpmnNamespace + "messageFlow")
            .FirstOrDefault(f => f.Attribute("sourceRef")?.Value == sourceId);
        var targetRef = flow?.Attribute("targetRef")?.Value;
        if (targetRef is null) return null;

        var target = document.Descendants()
            .FirstOrDefault(e => e.Name.Namespace == BpmnNamespace && e.Attribute("id")?.Value == targetRef);
        if (target is null || !IsValidMessageFlowTarget(target)) return null;
        if (target.Name.LocalName == "participant")
        {
            target = ParticipantMessageStart(target)!;
        }

        var targetProcessId = ProcessOf(target)?.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(targetProcessId)) return null;

        // The name the target answers to: its message, or -- for a receive task,
        // which has no message of its own -- its element id (#112).
        string? messageName;
        string? messageRef = null;
        if (target.Name.LocalName == "receiveTask")
        {
            messageName = target.Attribute("id")?.Value;
        }
        else
        {
            messageRef = target.Element(BpmnNamespace + "messageEventDefinition")?.Attribute("messageRef")?.Value;
            messageName = messageRef is not null && messageNamesById.TryGetValue(messageRef, out var resolved) ? resolved : null;
        }
        if (string.IsNullOrWhiteSpace(messageName)) return null;

        return (targetProcessId, messageName, messageRef,
            Trimmed(target.Attribute(FlowableNamespace + CorrelationKeyAttribute)?.Value));
    }

    private static void ApplyMessageFlows(XDocument document)
    {
        var messageNamesById = MessageNamesById(document);

        foreach (var source in document.Descendants().Where(IsValidMessageFlowSource).ToList())
        {
            var resolved = ResolveMessageFlowTarget(document, source, messageNamesById);
            if (resolved is null) continue;
            var (targetProcessId, messageName, messageRef, targetKey) = resolved.Value;

            // #648. ONE rule for all three: the flow fills a blank and never
            // overwrites what the author typed. The key had this rule from the
            // start (#170's discretion); the target and the name did not, so a
            // studio author's explicit target was rewritten on every save while
            // run time -- which reads the same attributes -- let it win.
            FillIfBlank(source, FlowableNamespace + TargetProcessKeyAttribute, targetProcessId);

            if (source.Name.LocalName == "sendTask")
            {
                FillIfBlank(source, FlowableNamespace + "autonateMessageName", messageName);
            }
            else if (messageRef is not null)
            {
                // A throw or end event names its message by reference; the flow
                // supplies one where the author left it off.
                var definition = source.Element(BpmnNamespace + "messageEventDefinition");
                if (definition is not null) FillIfBlank(definition, "messageRef", messageRef);
            }

            FillIfBlank(source, FlowableNamespace + CorrelationKeyAttribute, targetKey);
        }
    }

    private static void FillIfBlank(XElement element, XName attribute, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (string.IsNullOrWhiteSpace(element.Attribute(attribute)?.Value))
        {
            element.SetAttributeValue(attribute, value);
        }
    }

    /// <summary>
    /// A message flow that could never deliver is refused before deployment
    /// (#170): inside one pool, between endpoints that are not a send/receive
    /// pair, into a pool that deploys nothing, or between ids that do not exist.
    /// A source with MORE THAN ONE flow is refused too (#649): a send delivers
    /// to one target, so the second flow would be drawn and never sent.
    /// </summary>
    private static IReadOnlyList<string> BuildMessageFlowErrors(XDocument document)
    {
        var flows = document.Descendants(BpmnNamespace + "messageFlow").ToList();
        if (flows.Count == 0) return Array.Empty<string>();

        var byId = ElementsById(document);
        var errors = new List<string>();

        // #649. Fan-out. `ResolveMessageFlowTarget` takes the first flow leaving
        // a source, so a second one was silently dropped -- the send-into-the-void
        // shape this whole check exists to end. Refused by name until Auton8
        // implements one send reaching several pools.
        foreach (var group in flows
                     .Where(f => !string.IsNullOrWhiteSpace(f.Attribute("sourceRef")?.Value))
                     .GroupBy(f => f.Attribute("sourceRef")!.Value, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            var sourceLabel = byId.TryGetValue(group.Key, out var sourceElement) ? Label(sourceElement) : group.Key;
            var names = string.Join(", ", group.Select(f => $"'{f.Attribute("name")?.Value ?? f.Attribute("id")?.Value ?? "(unnamed)"}'"));
            errors.Add($"'{sourceLabel}' has {group.Count()} message flows leaving it ({names}). "
                + "A send delivers to one target, so only the first would ever be sent. "
                + "Keep one message flow per sender; to reach several pools, use one sender per pool.");
        }
        foreach (var flow in flows)
        {
            var flowName = flow.Attribute("name")?.Value ?? flow.Attribute("id")?.Value ?? "(unnamed message flow)";
            var sourceRef = flow.Attribute("sourceRef")?.Value;
            var targetRef = flow.Attribute("targetRef")?.Value;
            if (sourceRef is null || !byId.TryGetValue(sourceRef, out var source))
            {
                errors.Add($"Message flow '{flowName}' starts at '{sourceRef ?? "(nothing)"}', which is not in the diagram.");
                continue;
            }
            if (targetRef is null || !byId.TryGetValue(targetRef, out var target))
            {
                errors.Add($"Message flow '{flowName}' ends at '{targetRef ?? "(nothing)"}', which is not in the diagram.");
                continue;
            }

            if (!IsValidMessageFlowSource(source))
            {
                errors.Add($"Message flow '{flowName}' starts at '{Label(source)}', which does not send a message. "
                    + "A message flow must start at a send task, a message throw event or a message end event.");
            }
            XElement? targetProcess;
            if (target.Name.LocalName == "participant")
            {
                // Drawn to the pool itself. Deliverable only if the pool has
                // exactly one message start event; a pool with nothing in it is
                // the void, and one with several starts is ambiguous.
                var poolName = target.Attribute("name")?.Value ?? target.Attribute("id")?.Value ?? "(unnamed pool)";
                var processRef = target.Attribute("processRef")?.Value;
                targetProcess = processRef is null ? null
                    : document.Descendants(BpmnNamespace + "process").FirstOrDefault(p => p.Attribute("id")?.Value == processRef);
                if (targetProcess is null || !HasFlowNodes(targetProcess))
                {
                    errors.Add($"Message flow '{flowName}' sends into pool '{poolName}', which contains nothing to run and deploys as nothing. "
                        + "A message sent there would never arrive; draw the receiving flow inside the pool, or remove the message flow.");
                    continue;
                }
                if (ParticipantMessageStart(target) is null)
                {
                    errors.Add($"Message flow '{flowName}' ends at pool '{poolName}' itself, which has no single message start event to deliver to. "
                        + "End the flow at the element that receives it: a receive task, a message catch event, or a message start event.");
                    continue;
                }
            }
            else
            {
                targetProcess = ProcessOf(target);
                if (!IsValidMessageFlowTarget(target))
                {
                    errors.Add($"Message flow '{flowName}' ends at '{Label(target)}', which does not receive a message. "
                        + "A message flow must end at a receive task, a message catch event, a message boundary event or a message start event.");
                }
            }

            var sourceProcess = ProcessOf(source);
            if (sourceProcess is not null && ReferenceEquals(sourceProcess, targetProcess))
            {
                errors.Add($"Message flow '{flowName}' connects two elements in the same pool. "
                    + "A message flow crosses between pools; inside one pool, use a sequence flow.");
            }
            if (targetProcess is not null && !HasFlowNodes(targetProcess))
            {
                var poolName = document.Descendants(BpmnNamespace + "participant")
                    .FirstOrDefault(p => p.Attribute("processRef")?.Value == targetProcess.Attribute("id")?.Value)
                    ?.Attribute("name")?.Value ?? targetProcess.Attribute("id")?.Value ?? "(unnamed pool)";
                errors.Add($"Message flow '{flowName}' sends into pool '{poolName}', which contains nothing to run and deploys as nothing. "
                    + "A message sent there would never arrive; draw the receiving flow inside the pool, or remove the message flow.");
            }
        }
        return errors;
    }

    private static string Label(XElement element) =>
        element.Attribute("name")?.Value ?? element.Attribute("id")?.Value ?? element.Name.LocalName;

    /// <summary>
    /// What a collaboration would deploy as, participant by participant (#169).
    /// The prepare path reports it so the studio can say which pool starts and
    /// which pools deploy nothing.
    /// </summary>
    public static IReadOnlyList<WorkflowParticipantInfo> DescribeCollaboration(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<WorkflowParticipantInfo>();

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return Array.Empty<WorkflowParticipantInfo>();
        }

        var participants = document.Descendants(BpmnNamespace + "participant").ToList();
        if (participants.Count == 0) return Array.Empty<WorkflowParticipantInfo>();

        var primary = ResolvePrimaryProcess(document);
        var processesById = document.Descendants(BpmnNamespace + "process")
            .Where(p => !string.IsNullOrWhiteSpace(p.Attribute("id")?.Value))
            .GroupBy(p => p.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return participants.Select(participant =>
        {
            var processRef = participant.Attribute("processRef")?.Value;
            var process = processRef is not null && processesById.TryGetValue(processRef, out var found) ? found : null;
            return new WorkflowParticipantInfo(
                Id: participant.Attribute("id")?.Value ?? string.Empty,
                Name: participant.Attribute("name")?.Value ?? participant.Attribute("id")?.Value ?? "(unnamed pool)",
                ProcessId: processRef,
                IsExecutable: process is not null && HasFlowNodes(process),
                IsPrimary: process is not null && ReferenceEquals(process, primary));
        }).ToList();
    }

    /// <summary>
    /// A collaboration that could not deploy as a set is refused, naming what is
    /// wrong (#169). Replaces #578's "more than one pool" refusal, which existed
    /// because only one definition survived; now all of them do.
    /// </summary>
    private static IReadOnlyList<string> BuildCollaborationErrors(XDocument document)
    {
        var participants = document.Descendants(BpmnNamespace + "participant").ToList();
        if (participants.Count == 0)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        var processes = document.Descendants(BpmnNamespace + "process").ToList();

        var duplicateIds = processes
            .Select(p => p.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var id in duplicateIds)
        {
            errors.Add($"Two pools share the process id '{id}'. Each pool must be its own process, so give one of them a different id.");
        }

        var processIds = processes.Select(p => p.Attribute("id")?.Value).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var participant in participants)
        {
            var name = participant.Attribute("name")?.Value ?? participant.Attribute("id")?.Value ?? "(unnamed pool)";
            var processRef = participant.Attribute("processRef")?.Value;
            if (string.IsNullOrWhiteSpace(processRef))
            {
                errors.Add($"Pool '{name}' is not attached to a process. Every pool must reference the process it contains.");
            }
            else if (!processIds.Contains(processRef))
            {
                errors.Add($"Pool '{name}' references process '{processRef}', which is not in the diagram.");
            }
        }

        var executable = participants.Count(p =>
        {
            var processRef = p.Attribute("processRef")?.Value;
            var process = processRef is null ? null : processes.FirstOrDefault(x => x.Attribute("id")?.Value == processRef);
            return process is not null && HasFlowNodes(process);
        });
        if (executable == 0)
        {
            errors.Add("None of the pools contains anything to run. A collaboration needs at least one pool with a flow inside it; a pool drawn only to show a counterparty deploys as nothing.");
        }

        return errors;
    }

    private static IReadOnlyList<string> BuildUnsupportedElementErrors(
        XDocument document,
        BpmnSupportManifest support)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in document.Descendants())
        {
            if (node.Name.Namespace != BpmnNamespace) continue;

            foreach (var match in support.Match(node))
            {
                if (!match.CannotExecute) continue;
                if (!seen.Add(match.Name)) continue;

                var label = node.Attribute("name")?.Value ?? node.Attribute("id")?.Value;
                var where = label is null ? "" : $" ('{label}')";
                errors.Add(
                    $"{match.Name}{where} cannot be deployed: {match.Reason} " +
                    "Remove it from the diagram, or replace it with an element that executes.");
            }
        }

        return errors;
    }


    // #158: `EventSubProcessConditionalStartEventActivityBehavior` is the only
    // conditional start behaviour Flowable 8.0.0 ships, and its own validator
    // rejects a process-level conditional start with
    // `flowable-start-event-invalid-event-definition` — a message that tells an
    // author nothing about what to do.
    //
    // The element is fine; where the studio lets you put it is the problem. So this
    // refuses the invalid placement and names the constraint, rather than marking
    // the element unrunnable in the manifest — which would be wrong in the other
    // direction and would block #162's event-subprocess work.
    // #157: the three timer kinds a boundary event can carry, plus whether it
    // interrupts.
    //
    // Established against a live engine rather than assumed: a timer boundary does
    // NOT require `flowable:async` on the activity it is attached to. Four such
    // timers fired correctly on plain user tasks with no async anywhere, so nothing
    // here sets it.
    private static void ApplyTimerBoundaryEventSnapshot(XElement boundaryElement, WorkflowElementSnapshot snapshot)
    {
        var timer = boundaryElement.Element(BpmnNamespace + "timerEventDefinition");
        if (timer is null)
        {
            return;
        }

        var duration = NullIfBlank(snapshot.BoundaryTimerDuration);
        var date = NullIfBlank(snapshot.BoundaryTimerDate);
        var cycle = NullIfBlank(snapshot.BoundaryTimerCycle);

        // Only rewrite when the snapshot actually carries one. A snapshot with all
        // three absent describes some other element and must not blank this one.
        if (duration is not null || date is not null || cycle is not null)
        {
            // Clear every kind first. Flowable rejects a definition carrying two,
            // and a stale timeCycle beside a new timeDuration is a valid-looking
            // diagram that behaves unpredictably.
            timer.Element(BpmnNamespace + "timeDuration")?.Remove();
            timer.Element(BpmnNamespace + "timeDate")?.Remove();
            timer.Element(BpmnNamespace + "timeCycle")?.Remove();

            var (name, value) = duration is not null
                ? ("timeDuration", duration)
                : date is not null
                    ? ("timeDate", date)
                    : ("timeCycle", cycle!);

            timer.Add(new XElement(
                BpmnNamespace + name,
                new XAttribute(XsiNamespace + "type", FormalExpressionType(timer)),
                value));
        }

        if (snapshot.CancelActivity is { } cancelActivity)
        {
            // Written explicitly in both directions: BPMN defaults an absent
            // cancelActivity to true, so leaving it off to mean "interrupting"
            // would make a non-interrupting timer impossible to turn back.
            boundaryElement.SetAttributeValue("cancelActivity", cancelActivity ? "true" : "false");
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // #157: a timer boundary event carrying no time is a hang.
    //
    // It deploys cleanly, produces no job, and simply never fires — so the author
    // sees an activity that waits forever with nothing to show why. Epic #40's rule
    // is that a hang is a defect rather than a documented behaviour, so this is
    // refused at publish.
    // #161: an embedded subprocess with no start event cannot be entered.
    //
    // It deploys cleanly and then fails when the process reaches it, with a 500 and
    // "No initial activity found for subprocess <id>" — a failure that lands on
    // whoever *ran* the process rather than on the author who published it.
    // Established by deploying one; the story's premise said it hangs, and it does
    // not, but the remedy is the same because the person who sees the failure is the
    // wrong person.
    //
    // Deliberately NOT a rule about missing end events. A subprocess whose inner
    // flow simply stops **works correctly** — Flowable completes it once no tokens
    // remain inside, verified by running one. Refusing that would break diagrams
    // that run today.
    //
    // Event subprocesses are excluded: they are triggered rather than entered, and
    // #162 owns their own start-event rule.
    // #167: a manual task and a plain task both deploy and pass straight through.
    //
    // Verified by running both against Flowable 8.0.0: the process reached the
    // activity beyond without creating a task or pausing anywhere.
    // `ManualTaskActivityBehavior` is 488 bytes, and BPMN specifies a manual task as
    // work done outside the system with no engine involvement; a plain `bpmn:task`
    // is the same. So a process containing either reaches its end having done
    // nothing a person was meant to do.
    //
    // The studio converts both to user tasks at design time. This is the backstop
    // for diagrams the studio never touched — a hand-edited file, or one imported
    // straight to the API.
    // Written by the studio when it converts a manual or generic task, so the
    // unassignable rule below applies to exactly those and to nothing else.
    // `flowable:` because bpmn-js loads no moddle extension for our own namespace —
    // raw prefixed attributes in $attrs are the only round-trip-safe shape.
    internal const string ConvertedFromAttribute = "autonateConvertedFrom";

    private static readonly HashSet<string> NonWaitingTaskElementNames =
    [
        "manualTask",
        "task"
    ];

    private static IReadOnlyList<string> BuildNonWaitingTaskErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var element in document.Descendants())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;

            if (NonWaitingTaskElementNames.Contains(element.Name.LocalName))
            {
                var kind = element.Name.LocalName == "manualTask" ? "Manual task" : "Task";
                errors.Add(
                    $"{kind} '{LabelOf(element)}' cannot be deployed: it looks like a step " +
                    "somebody performs, but the engine passes straight through it without " +
                    "waiting for anyone. Auton8 runs work through user tasks — the studio " +
                    "converts these automatically, so this diagram was authored elsewhere.");
                continue;
            }

            // A converted task nobody can do. Scoped to user tasks, and deliberately
            // NOT applied to every user task in the product: an unassigned task is a
            // first-class state elsewhere (the execution view renders "(unassigned)"),
            // and a blanket rule would refuse workflows that run today.
            //
            // The marker is what the studio writes when it converts, so this catches
            // exactly the tasks this story created and nothing else.
            var convertedFrom = element.Attribute(FlowableNamespace + ConvertedFromAttribute)?.Value;
            if (element.Name.LocalName == "userTask"
                && !string.IsNullOrWhiteSpace(convertedFrom)
                && !HasSomeoneToDoIt(element))
            {
                errors.Add(
                    $"User task '{LabelOf(element)}' has nobody to do it. It was converted " +
                    "from a task the engine cannot wait on, so it needs an assignee or " +
                    "candidate users or groups before it can be published.");
            }
        }

        return errors;
    }

    /// <summary>
    /// The attribute on a <c>bpmn:lane</c> naming the Auton8 group its user
    /// tasks default to (#171). In the <c>autonate</c> namespace, which is on
    /// the do-not-rename list; this adds an attribute to it and changes nothing
    /// already there.
    /// </summary>
    public const string LaneGroupAttribute = "groupId";

    /// <summary>A lane and the group it names (#171).</summary>
    public sealed record WorkflowLaneGroup(string LaneId, string LaneName, string GroupId);

    /// <summary>
    /// Every lane carrying a group association (#171), so publish can refuse a
    /// lane whose group no longer exists by name rather than deploy tasks
    /// nobody can see.
    /// </summary>
    public static IReadOnlyList<WorkflowLaneGroup> ExtractLaneGroups(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<WorkflowLaneGroup>();

        var document = XDocument.Parse(xml);
        return document.Descendants(BpmnNamespace + "lane")
            .Select(lane => (Lane: lane, GroupId: LaneGroupOf(lane)))
            .Where(pair => pair.GroupId is not null)
            .Select(pair => new WorkflowLaneGroup(
                pair.Lane.Attribute("id")?.Value ?? string.Empty,
                LabelOf(pair.Lane),
                pair.GroupId!))
            .ToList();
    }

    private static string? LaneGroupOf(XElement lane)
    {
        var value = lane.Attribute(ScriptTaskIdentity.AutoNateNamespace + LaneGroupAttribute)?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// A lane's group is the default assignment of every user task it lists
    /// (#171): <c>flowable:candidateGroups</c> on each one that has no
    /// assignee, candidate users or candidate groups of its own. A task that
    /// has any of those keeps them -- the lane is a default beneath the task's
    /// own settings, never a constraint above them.
    /// </summary>
    /// <remarks>
    /// A task inside a NESTED lane is listed by the inner lane and by every
    /// lane around it -- bpmn-js collects every lane whose bounds contain the
    /// shape -- so the innermost lane that names a group wins. A lane with no
    /// group contributes nothing, and a task no lane lists is left exactly as
    /// authored, which is what "moving it out of all lanes leaves no stale
    /// group" means on the deployed copy.
    /// </remarks>
    /// <summary>
    /// Every participant whose process contains no flow node is marked
    /// non-executable (#645), so the engine deploys the diagram and creates no
    /// definition for the empty pool. Idempotent with prepare's own marking.
    /// </summary>
    private static void NeutraliseEmptyPools(XDocument document)
    {
        var processesById = document.Descendants(BpmnNamespace + "process")
            .Where(p => !string.IsNullOrWhiteSpace(p.Attribute("id")?.Value))
            .GroupBy(p => p.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var participant in document.Descendants(BpmnNamespace + "participant"))
        {
            var processRef = participant.Attribute("processRef")?.Value;
            if (string.IsNullOrWhiteSpace(processRef) || !processesById.TryGetValue(processRef, out var process)) continue;
            if (!HasFlowNodes(process))
            {
                process.SetAttributeValue("isExecutable", "false");
            }
        }
    }

    private static void ApplyLaneAssignments(XDocument document)
    {
        var elementsById = ElementsById(document);
        var groupByTask = new Dictionary<string, (int Depth, string GroupId)>(StringComparer.Ordinal);

        foreach (var lane in document.Descendants(BpmnNamespace + "lane"))
        {
            var groupId = LaneGroupOf(lane);
            if (groupId is null) continue;

            var depth = lane.Ancestors(BpmnNamespace + "lane").Count();
            foreach (var reference in lane.Elements(BpmnNamespace + "flowNodeRef"))
            {
                var taskId = reference.Value.Trim();
                if (taskId.Length == 0) continue;
                if (!groupByTask.TryGetValue(taskId, out var current) || current.Depth < depth)
                {
                    groupByTask[taskId] = (depth, groupId);
                }
            }
        }

        foreach (var (taskId, assignment) in groupByTask)
        {
            if (!elementsById.TryGetValue(taskId, out var element)) continue;
            if (element.Name.LocalName != "userTask" || HasSomeoneToDoIt(element)) continue;
            element.SetAttributeValue(FlowableNamespace + "candidateGroups", assignment.GroupId);
        }
    }

    private static bool HasSomeoneToDoIt(XElement userTask) =>
        new[] { "assignee", "candidateUsers", "candidateGroups" }
            .Any(name => !string.IsNullOrWhiteSpace(userTask.Attribute(FlowableNamespace + name)?.Value));

    private static IReadOnlyList<string> BuildSubProcessValidationErrors(XDocument document)
    {
        var errors = new List<string>();
        var containers = new[] { "subProcess", "transaction", "adHocSubProcess" };

        foreach (var container in document.Descendants()
                     .Where(e => e.Name.Namespace == BpmnNamespace
                                 && containers.Contains(e.Name.LocalName, StringComparer.Ordinal)))
        {
            if (string.Equals(container.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // An ad-hoc subprocess has no sequence flows by design — its activities
            // are chosen at runtime — so it is exempt from needing a start event.
            if (string.Equals(container.Name.LocalName, "adHocSubProcess", StringComparison.Ordinal))
            {
                if (!container.Elements().Any(child => child.Name.Namespace == BpmnNamespace))
                {
                    errors.Add(BuildEmptyMessage(container, "ad-hoc subprocess"));
                }
                continue;
            }

            if (container.Element(BpmnNamespace + "startEvent") is null)
            {
                var label = LabelOf(container);
                var kind = container.Name.LocalName == "transaction" ? "Transaction" : "Subprocess";
                errors.Add(
                    $"{kind} '{label}' has no start event, so the engine cannot enter it. " +
                    "Add a start event inside it — without one the process fails when it " +
                    "reaches this subprocess, and the failure lands on whoever ran it rather " +
                    "than on you.");
            }
        }

        return errors;
    }

    // #114. An error thrown with a code no boundary event catches does not hang and
    // does not continue quietly — Flowable takes the WHOLE INSTANCE down:
    //
    //   POST /runtime/process-instances -> 500
    //   "No catching boundary event found for error with errorCode 'X',
    //    neither in same process nor in parent process"
    //
    // No instance, no history, nothing on the execution error surface, and the
    // failure lands on whoever started it. The issue pre-decided that an instance
    // disappearing is a defect to fix rather than a behaviour to document.
    //
    // It is fully detectable from the XML, so it is refused at publish. That turns
    // a vanished production instance into a sentence an author reads while they
    // still have the diagram open — which is the same argument the rest of this
    // story makes about codes having to match.
    //
    // Escalation is deliberately NOT included. An uncaught escalation is not an
    // error in BPMN: it is a notification nobody subscribed to, the engine carries
    // on, and refusing it would block a legitimate diagram.
    /// <summary>
    /// The rules enforced at PUBLISH, not merely at prepare.
    /// </summary>
    /// <remarks>
    /// `ValidateProcess` runs on /prepare, which the studio calls before saving.
    /// /publish is what actually deploys, and a caller that publishes without
    /// preparing reaches the engine unchecked — so every rule in that set is
    /// advisory (#225 puts the general question to the user).
    ///
    /// This is the deliberately small set promoted to the publish path. The
    /// criterion for membership, so it does not grow by habit: **the engine either
    /// destroys something or accepts a diagram that cannot work, and the author
    /// gets no usable diagnosis.**
    ///
    ///   #114 — an error nobody catches. Flowable answers the start call with 500
    ///          and the instance never exists: no history, nothing on the error
    ///          surface, and the failure lands on whoever ran it.
    ///   #164 — an event-based gateway that cannot resolve. A single-path gateway
    ///          deploys cleanly and then waits forever; a bad target is refused by
    ///          the engine, but as a parse error naming a line and column.
    ///
    /// Everything else stays on prepare until #225 is decided. Moving the whole
    /// set changes what publish accepts for every diagram already in flight, which
    /// is a contract change and not a side effect of whichever story noticed it.
    /// </remarks>
    public static IReadOnlyList<string> ValidateStructureForPublish(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<string>();

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            // Malformed XML is the deploy path's problem to report; these checks
            // have nothing to say about it and must not mask it.
            return Array.Empty<string>();
        }

        return BuildStructureErrors(document);
    }

    /// <summary>
    /// The rules whose failure mode is severe enough that they must run wherever
    /// validation runs at all.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="ValidateProcess"/> and
    /// <see cref="ValidateStructureForPublish"/> rather than duplicated, because
    /// they diverged once already: #225 pointed publish at ValidateProcess, which
    /// did not contain these, and three rules stopped running with nothing to say
    /// so.
    /// </remarks>
    private static IReadOnlyList<string> BuildStructureErrors(XDocument document) =>
        [
            .. BuildUncaughtThrownCodeErrors(document),
            .. BuildEventBasedGatewayErrors(document),
            // #162 — an event subprocess that can never trigger, or one promising
            // not to interrupt when the engine will interrupt anyway. Both deploy
            // cleanly and neither tells the author anything.
            .. BuildEventSubProcessErrors(document),
            // #115 — a compensation handler that waits. Not a style question:
            // it crashes the engine mid-completion. See BuildCompensationErrors.
            .. BuildCompensationErrors(document),
            // #245 — a multi-instance marker configured two ways at once, where
            // the engine quietly honours one of them.
            .. BuildMultiInstanceErrors(document),
            // #270 — one signal name asked to carry two scopes, which Flowable
            // refuses to deploy at all.
            .. BuildSignalScopeErrors(document)
        ];

    /// <summary>
    /// One signal name cannot carry two scopes (#270).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable validates <c>flowable-signal-duplicate-name</c> at deployment, so
    /// two <c>&lt;bpmn:signal&gt;</c> roots sharing a name are refused with a 500
    /// — measured against 8.0.0, where two roots with DISTINCT names deploy
    /// cleanly. Scope lives on the root, so one name means one scope.
    /// </para>
    /// <para>
    /// #244 tried to have it both ways: it cloned the root for a scoped catch so a
    /// signal start event sharing the name kept its global subscription. The clone
    /// carried the same name, and the result would not deploy — the diagram that
    /// fix existed to support became unpublishable, and its test never noticed
    /// because it asserted the XML tree instead of deploying it.
    /// </para>
    /// <para>
    /// This is a modelling contradiction, so it is reported as one. A start event
    /// must be global to start instances from outside; asking the same name to be
    /// instance-scoped elsewhere cannot be honoured by any engine, and the author
    /// is the only one who can resolve it.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> BuildSignalScopeErrors(XDocument document)
    {
        foreach (var (name, use) in CollectSignalScopeUses(document))
        {
            // #279. Two roots sharing a name is refused by Flowable at deployment
            // — flowable-signal-duplicate-name, a 500 with no usable message —
            // and nothing here caught it, so the studio said "published" and the
            // deploy failed. Ids differ, names do not; the engine keys on name.
            if (use.Roots.Count > 1)
            {
                var ids = string.Join("', '", use.Roots
                    .Select(root => root.Attribute("id")?.Value ?? "(no id)"));

                yield return
                    $"The diagram declares the signal '{name}' {use.Roots.Count} times " +
                    $"(as '{ids}'). Flowable identifies a signal by its name and refuses a " +
                    "deployment that declares one name twice, so give them different names " +
                    "or delete the duplicates.";
            }

            // #278. A spelling nothing recognises used to be read as "not
            // instance", i.e. global — so a mistyped `instnace` published clean
            // and ran engine-wide, which is the exact leak the scope exists to
            // prevent. Silence about a typo is the worst of the three options.
            foreach (var (label, raw) in use.Unrecognised)
            {
                yield return
                    $"'{label}' asks for the signal '{name}' to be scoped '{raw}', which is " +
                    "not a scope Auton8 understands. Use 'instance' so only this process " +
                    "instance hears it, or 'global' so every instance does.";
            }

            if (use.DeclaredBy.Count <= 1) continue;

            var scoped = string.Join("', '",
                use.DeclaredBy.GetValueOrDefault(SignalScopeDeclaration.Instance, []));
            var global = string.Join("', '",
                use.DeclaredBy.GetValueOrDefault(SignalScopeDeclaration.Global, []));

            yield return
                $"The signal '{name}' is scoped to this instance by '{scoped}', and is also " +
                $"used by '{global}', which needs it global. Flowable allows one scope per " +
                "signal name and refuses a diagram that declares two, so give one of them a " +
                "different signal name, or put them both on the same scope.";
        }
    }

    /// <summary>
    /// Multi-instance settings the engine would silently ignore (#245).
    /// </summary>
    /// <remarks>
    /// Both of these deploy cleanly and run, which is what makes them worth
    /// refusing: the author sees a field they filled in and a result that does not
    /// reflect it, with nothing anywhere saying why.
    /// </remarks>
    private static IEnumerable<string> BuildMultiInstanceErrors(XDocument document)
    {
        foreach (var loop in document.Descendants(BpmnNamespace + "multiInstanceLoopCharacteristics"))
        {
            var owner = LabelOf(loop.Parent) ?? "a step";

            var hasCollection = DeclaresCollection(loop);
            var hasCardinality = DeclaresCardinality(loop);

            if (hasCollection && hasCardinality)
            {
                // Flowable reads the collection and ignores the count, so the
                // author gets one instance per item having asked for exactly N.
                yield return
                    $"'{owner}' repeats over a list AND has a fixed number of runs. " +
                    "Flowable uses the list and ignores the number, so one of them " +
                    "would silently do nothing. Clear whichever you did not mean.";
            }

            var target = AggregationTarget(loop);
            var source = AggregationSource(loop);

            // #364: an author who hand-wrote <flowable:variableAggregation> HAS
            // said which variable to collect, in the spelling the expansion
            // respects. This rule read only the attributes and refused them.
            if (target is not null && source is null && !DeclaresAggregationElement(loop))
            {
                yield return
                    $"'{owner}' collects each run's result into '{target}' but does " +
                    "not say which variable to collect. Name the variable each run " +
                    "sets, or clear the collection target.";
            }

            if (source is not null && target is null && !DeclaresAggregationElement(loop))
            {
                yield return
                    $"'{owner}' collects the variable '{source}' from each run but " +
                    "does not say where to put the results. Name a variable to " +
                    "collect them into, or clear the variable.";
            }
        }
    }

    // #242. BPMN matches an error by its errorCode, not by the id of the
    // <bpmn:error> root that carries it.
    //
    // Comparing errorRef ids refused diagrams Flowable runs correctly: two roots
    // sharing one errorCode under different ids read as non-matching. The studio
    // reuses one root per code so it never bit an Auton8-authored diagram — only
    // hand-authored and imported ones, which is the population the "imported
    // diagram" criterion exists for.
    //
    // Falls back to the ref itself when no root declares it, so a dangling ref
    // still compares equal to another dangling ref of the same name rather than
    // silently matching everything.
    private static string ResolveErrorCode(XDocument document, string errorRef)
    {
        var root = document.Descendants(BpmnNamespace + "error")
            .FirstOrDefault(e => e.Attribute("id")?.Value == errorRef);
        return Trimmed(root?.Attribute("errorCode")?.Value) ?? errorRef;
    }

    private static IReadOnlyList<string> BuildUncaughtThrownCodeErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var process in document.Descendants(BpmnNamespace + "process"))
        {
            // Codes catchable anywhere in this process, at any depth. BPMN
            // propagates an error outward to enclosing scopes, so a boundary event
            // anywhere up the chain catches it — which is why the whole process is
            // one pool rather than each subprocess being checked in isolation.
            var caught = process
                .Descendants(BpmnNamespace + "boundaryEvent")
                .Elements(BpmnNamespace + "errorEventDefinition")
                .Select(definition => definition.Attribute("errorRef")?.Value)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => ResolveErrorCode(document, code!))
                .ToHashSet(StringComparer.Ordinal);

            // An error start event inside an event subprocess catches too (#162's
            // territory). Counted here so this validation does not reject a
            // diagram that handles its error that way.
            foreach (var eventSubProcess in process.Descendants(BpmnNamespace + "subProcess")
                         .Where(sp => string.Equals(
                             sp.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var definition in eventSubProcess
                             .Descendants(BpmnNamespace + "startEvent")
                             .Elements(BpmnNamespace + "errorEventDefinition"))
                {
                    var code = definition.Attribute("errorRef")?.Value;
                    // An error start event with no errorRef catches ANY error, so
                    // once one exists nothing in this process is uncatchable.
                    if (string.IsNullOrWhiteSpace(code)) { caught.Clear(); caught.Add("*"); }
                    else caught.Add(ResolveErrorCode(document, code));
                }
            }

            if (caught.Contains("*")) continue;

            foreach (var throwing in process.Descendants(BpmnNamespace + "endEvent")
                         .Where(e => e.Elements(BpmnNamespace + "errorEventDefinition").Any()))
            {
                var code = throwing.Elements(BpmnNamespace + "errorEventDefinition")
                    .Select(d => d.Attribute("errorRef")?.Value)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

                if (string.IsNullOrWhiteSpace(code)) continue;
                var thrownCode = ResolveErrorCode(document, code!);
                if (caught.Contains(thrownCode)) continue;

                errors.Add(
                    // The CODE the author typed, not the ref id — the id means
                    // nothing to someone reading their own diagram.
                    $"The error end event '{LabelOf(throwing)}' raises '{thrownCode}', and nothing in " +
                    $"'{LabelOf(process)}' catches it. Add an error boundary event carrying the " +
                    "same code to the activity it should interrupt. Published as-is, reaching " +
                    "this event destroys the whole process instance — there is no history to " +
                    "look at afterwards and the failure lands on whoever started it.");
            }
        }

        return errors;
    }

    // #164. Two rules, and only one of them duplicates the engine.
    //
    // Flowable rejects a bad TARGET itself
    // ('flowable-event-gateway-only-connected-to-intermediate-events'), but as a
    // parse error at deploy, which names a line and column rather than telling an
    // author what to do. Ours says it earlier and in their terms.
    //
    // Flowable does NOT object to a gateway with one outgoing flow — verified,
    // that deploys cleanly. A choice between one thing is a diagram that waits
    // forever on a single event while looking like it offers alternatives, so
    // that rule is genuinely ours.
    //
    // Receive tasks are refused DESPITE BPMN allowing them after an event-based
    // gateway, because this engine does not: verified, `flowable:` rejects the
    // deployment. Saying so here is better than letting the author discover it as
    // a parse error.
    private static IReadOnlyList<string> BuildEventBasedGatewayErrors(XDocument document)
    {
        var errors = new List<string>();

        var flowsBySource = document
            .Descendants(BpmnNamespace + "sequenceFlow")
            .Where(flow => !string.IsNullOrWhiteSpace(flow.Attribute("sourceRef")?.Value))
            .GroupBy(flow => flow.Attribute("sourceRef")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var elementsById = document
            .Descendants()
            .Where(e => e.Name.Namespace == BpmnNamespace
                        && !string.IsNullOrWhiteSpace(e.Attribute("id")?.Value))
            .GroupBy(e => e.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var gateway in document.Descendants(BpmnNamespace + "eventBasedGateway"))
        {
            var gatewayId = gateway.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(gatewayId)) continue;

            var label = LabelOf(gateway);
            var outgoing = flowsBySource.TryGetValue(gatewayId!, out var flows)
                ? flows
                : new List<XElement>();

            if (outgoing.Count < 2)
            {
                errors.Add(
                    $"The event-based gateway '{label}' has {outgoing.Count} outgoing " +
                    (outgoing.Count == 1 ? "path" : "paths") +
                    ", so there is nothing for it to choose between. Give it at least two " +
                    "events to wait for, or use a plain intermediate catch event instead — as " +
                    "drawn, the process waits on one event while the diagram suggests it is " +
                    "waiting on several.");
            }

            foreach (var flow in outgoing)
            {
                var targetId = flow.Attribute("targetRef")?.Value;
                if (string.IsNullOrWhiteSpace(targetId)
                    || !elementsById.TryGetValue(targetId!, out var target))
                {
                    continue;
                }

                if (string.Equals(target.Name.LocalName, "intermediateCatchEvent", StringComparison.Ordinal))
                {
                    continue;
                }

                var targetLabel = LabelOf(target);
                errors.Add(string.Equals(target.Name.LocalName, "receiveTask", StringComparison.Ordinal)
                    ? $"The event-based gateway '{label}' leads to the receive task " +
                      $"'{targetLabel}'. BPMN allows that, but this engine does not — it accepts " +
                      "only intermediate catch events after an event-based gateway, and refuses " +
                      "the whole deployment otherwise. Use a message intermediate catch event " +
                      "instead."
                    : $"The event-based gateway '{label}' leads to '{targetLabel}', which is not " +
                      "an event. Every path out of an event-based gateway must start with an " +
                      "intermediate catch event — that is what it waits on. As drawn, this " +
                      "diagram cannot be deployed.");
            }
        }

        return errors;
    }

    // #162. An event subprocess starts when its own start event triggers, never by
    // a sequence flow. Two shapes deploy cleanly and can never trigger, so the
    // engine will not catch either:
    //
    //   * no start event at all — nothing to trigger on;
    //   * a plain NONE start event — the shape a normal subprocess uses, which
    //     inside triggeredByEvent="true" means "starts on nothing".
    //
    // Both look reasonable in the diagram, which is what makes them worth
    // refusing: an event subprocess that silently never runs is indistinguishable
    // from one whose event never happened.
    private static IReadOnlyList<string> BuildEventSubProcessErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var eventSubProcess in document.Descendants(BpmnNamespace + "subProcess")
                     .Where(sp => string.Equals(
                         sp.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            var label = LabelOf(eventSubProcess);

            // Direct children only. A start event nested inside a subprocess
            // WITHIN this handler starts that inner scope, not this one, and
            // counting it would accept a handler that still cannot trigger.
            var startEvents = eventSubProcess.Elements(BpmnNamespace + "startEvent").ToList();

            if (startEvents.Count == 0)
            {
                errors.Add(
                    $"The event subprocess '{label}' has no start event, so nothing can ever " +
                    "trigger it. Give it a start event of the type it should react to — an " +
                    "error, message, timer, signal, escalation or condition. As drawn it " +
                    "deploys and never runs, which looks the same as its event never happening.");
                continue;
            }

            foreach (var startEvent in startEvents)
            {
                var hasDefinition = startEvent.Elements()
                    .Any(child => child.Name.Namespace == BpmnNamespace
                                  && child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal));

                var isError = startEvent.Elements(BpmnNamespace + "errorEventDefinition").Any();
                if (isError && string.Equals(
                        startEvent.Attribute("isInterrupting")?.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"The event subprocess '{label}' catches an error and is marked as not " +
                        "interrupting, which BPMN does not allow — an error always interrupts " +
                        "the scope it escapes from. Verified: the engine interrupts regardless, " +
                        "so as drawn the diagram promises something it does not do. Remove the " +
                        "setting, or catch an escalation instead if the work should carry on.");
                }

                if (hasDefinition) continue;

                errors.Add(
                    $"The event subprocess '{label}' starts with a plain start event, which " +
                    "means it starts on nothing. An event subprocess is entered by its event, " +
                    "never by a sequence flow — give the start event an error, message, timer, " +
                    "signal, escalation or condition definition, or make this an ordinary " +
                    "subprocess.");
            }
        }

        return errors;
    }

    private static string BuildEmptyMessage(XElement container, string kind) =>
        $"The {kind} '{LabelOf(container)}' is empty. Put at least one activity inside it, " +
        "or remove it — an empty one cannot do anything when the process reaches it.";

    private static string LabelOf(XElement element) =>
        element.Attribute("name")?.Value is { Length: > 0 } name
            ? name
            : element.Attribute("id")?.Value ?? "(unnamed)";

    private static IReadOnlyList<string> BuildTimerBoundaryEventValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var boundary in document.Descendants(BpmnNamespace + "boundaryEvent"))
        {
            var timer = boundary.Element(BpmnNamespace + "timerEventDefinition");
            if (timer is null) continue;

            var kinds = new[] { "timeDuration", "timeDate", "timeCycle" }
                .Select(name => timer.Element(BpmnNamespace + name))
                .Where(element => element is not null && !string.IsNullOrWhiteSpace(element.Value))
                .ToArray();

            var label = boundary.Attribute("name")?.Value ?? boundary.Attribute("id")?.Value ?? "(unnamed)";

            if (kinds.Length == 0)
            {
                errors.Add(
                    $"Timer boundary event '{label}' has no time set. Give it a duration " +
                    "(PT15M), a date (2026-12-31T09:00:00) or a repeating cycle (R3/PT1H) — " +
                    "without one it deploys, never fires, and the activity it guards waits forever.");
            }
            else if (kinds.Length > 1)
            {
                // Flowable rejects this at deployment with a parse error that names
                // the definition rather than the event, which is not actionable.
                errors.Add(
                    $"Timer boundary event '{label}' sets more than one kind of time. " +
                    "Choose a duration, a date or a cycle — not several.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Start events that are legal only inside an event subprocess (#289).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Error, escalation and conditional start events all react to something that
    /// happens <b>while a process is already running</b>, so BPMN allows them only
    /// inside <c>&lt;subProcess triggeredByEvent="true"&gt;</c>. Flowable enforces
    /// that, and enforces it brutally: a misplaced one is
    /// <c>flowable-start-event-invalid-event-definition</c> and the <b>whole
    /// deployment</b> is refused, not just its branch. An author's entire workflow
    /// fails to publish behind a green studio.
    /// </para>
    /// <para>
    /// This rule covered <b>conditional only</b> until #289. Error and escalation
    /// start events carry <c>studio: supported</c> manifest rows — correctly, since
    /// #162 ships them inside event subprocesses — so
    /// <c>BuildUnsupportedElementErrors</c> matched the row, found
    /// <c>engine: executes</c>, and passed them at any placement:
    /// </para>
    /// <code>
    /// ESCPROBE errors=0 warnings=0
    /// engine  -> flowable-start-event-invalid-event-definition
    /// </code>
    /// <para>
    /// The structural point, recorded because it will recur: the manifest keys on
    /// <c>(localName, eventDefinition)</c> and has <b>no container axis</b>, so a
    /// legal element in an illegal place is invisible to every guard built on it —
    /// including #282's, which asserts a row exists and this element's row is
    /// correct. Placement is a separate axis and needs separate rules; this is the
    /// one place they live.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Which container a start event may carry which event definition in (#309).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Placement is a <b>two-dimensional</b> constraint and every previous version
    /// of this rule modelled one dimension. #158 handled `conditional × process
    /// level`. #289 widened the definition axis to `{error, escalation,
    /// conditional}` and left the container axis binary — process-level vs event
    /// subprocess — so a **plain embedded subprocess** was neither, and fell
    /// through as though it were legal.
    /// </para>
    /// <para>
    /// The cell that bit is <c>{message, timer, signal} × plain subProcess</c>:
    /// `errors=0` from publish, and Flowable refuses the <b>whole deployment</b>
    /// with <c>flowable-subprocess-start-event-event-definition-not-allowed</c>.
    /// Reachable in four clicks, every step <c>studio: supported</c> — place a
    /// sub-process, morph it to an event sub-process, give its start event a
    /// timer, morph the container back. <b>The container morphs and the start
    /// event does not.</b>
    /// </para>
    /// <para>
    /// So the rule is a table keyed on the container, and the exhaustiveness is
    /// checked by <c>StartEventPlacementDifferentialTests</c>, which deploys every
    /// cell to a live engine and asserts this function agrees with it. That test
    /// enumerates nothing by hand, which is the property three hand-written
    /// versions of this rule did not have.
    /// </para>
    /// </remarks>
    private enum StartEventContainer
    {
        /// <summary>Directly in a <c>&lt;bpmn:process&gt;</c>.</summary>
        Process,

        /// <summary><c>&lt;subProcess triggeredByEvent="true"&gt;</c>.</summary>
        EventSubProcess,

        /// <summary>
        /// Any other container — a plain embedded subprocess, an ad-hoc
        /// subprocess, a transaction. Flowable allows a **bare** start event here
        /// and nothing else.
        /// </summary>
        Embedded
    }

    /// <summary>Event definitions each container permits on a start event.</summary>
    /// <remarks>
    /// Established by deploying every cell to Flowable 8.0.0 rather than from the
    /// specification, because the engine is what refuses the deployment. An empty
    /// local name means a bare start event with no definition at all.
    /// </remarks>
    private static readonly Dictionary<StartEventContainer, HashSet<string>> StartEventDefinitionsAllowed =
        new()
        {
            // A process is started from outside, so it may react to arriving
            // messages, clocks and signals -- and to nothing that only exists
            // once an instance is already running.
            [StartEventContainer.Process] = new(StringComparer.Ordinal)
            {
                "", "messageEventDefinition", "timerEventDefinition", "signalEventDefinition"
            },

            // An event subprocess is triggered from INSIDE a running instance, so
            // it is the one container that takes error, escalation and
            // conditional. A bare start event there would never trigger at all,
            // and BuildEventSubProcessErrors refuses that separately.
            [StartEventContainer.EventSubProcess] = new(StringComparer.Ordinal)
            {
                "", "messageEventDefinition", "timerEventDefinition", "signalEventDefinition",
                "conditionalEventDefinition", "errorEventDefinition", "escalationEventDefinition",

                // #321. `compensateEventDefinition` was here and the engine refuses
                // it -- `flowable-event-subprocess-invalid-start-event-definition`,
                // measured. The row was wrong from the day it was written, and the
                // differential test could not see it because the manifest already
                // withdraws Compensation Start Event for an unrelated reason (#107),
                // so an unrelated refusal satisfied the oracle.
                //
                // Promoting that one manifest row -- one word, no code -- would have
                // turned this into a live missed refusal. The cell now exists.
            },

            // A plain embedded subprocess is entered by a token arriving on a
            // sequence flow. There is nothing for a start event to react to, so
            // Flowable permits only a bare one.
            [StartEventContainer.Embedded] = new(StringComparer.Ordinal) { "" },
        };

    /// <summary>How an author refers to each definition, and where it may live.</summary>
    private static readonly Dictionary<string, string> StartEventDefinitionNouns =
        new(StringComparer.Ordinal)
        {
            ["messageEventDefinition"] = "Message",
            ["timerEventDefinition"] = "Timer",
            ["signalEventDefinition"] = "Signal",
            ["conditionalEventDefinition"] = "Conditional",
            ["errorEventDefinition"] = "Error",
            ["escalationEventDefinition"] = "Escalation",
            ["compensateEventDefinition"] = "Compensation",
        };

    internal static StartEventContainerKind ContainerKindOf(XElement start) =>
        (StartEventContainerKind)(int)ContainerOf(start);

    private static StartEventContainer ContainerOf(XElement start)
    {
        var container = start.Parent;
        if (container is null || container.Name == BpmnNamespace + "process")
        {
            return StartEventContainer.Process;
        }

        if (container.Name == BpmnNamespace + "subProcess"
            && string.Equals(
                container.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return StartEventContainer.EventSubProcess;
        }

        return StartEventContainer.Embedded;
    }

    /// <summary>Public mirror of <see cref="StartEventContainer"/>, for the tests.</summary>
    internal enum StartEventContainerKind
    {
        Process = 0,
        EventSubProcess = 1,
        Embedded = 2
    }

    private static IReadOnlyList<string> BuildStartEventPlacementErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var start in document.Descendants(BpmnNamespace + "startEvent"))
        {
            var definition = start.Elements()
                .FirstOrDefault(child => child.Name.Namespace == BpmnNamespace
                    && child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal));

            var localName = definition?.Name.LocalName ?? "";
            var container = ContainerOf(start);

            if (StartEventDefinitionsAllowed[container].Contains(localName)) continue;

            // A definition we have no noun for is still refused -- silence about an
            // element we do not recognise is how #282 and #289 both happened -- but
            // it is named by its raw type so the message stays honest.
            var noun = StartEventDefinitionNouns.GetValueOrDefault(
                localName, localName.Length == 0 ? "Plain" : localName);

            var (where, remedy) = container switch
            {
                StartEventContainer.Process => (
                    "start a process",
                    "It reacts to something that only exists once an instance is running, so it " +
                    "belongs inside an event subprocess. To start a process this way, start it " +
                    "another way and wait on an intermediate catch event instead."),
                StartEventContainer.EventSubProcess => (
                    "start an event subprocess",
                    "Choose a trigger an event subprocess can react to."),
                _ => (
                    "start an embedded subprocess",
                    "A subprocess is entered by a token arriving on a sequence flow, so its start " +
                    "event has nothing to react to and Flowable allows only a plain one. Move the " +
                    "trigger to a boundary event on the subprocess, or make the container an event " +
                    "subprocess."),
            };

            // The phrase "cannot start " is this rule's signature, and
            // StartEventPlacementDifferentialTests matches on it. It is deliberately
            // narrower than "start event": the #115 descope message reads
            // "Compensation Start Event ('x') cannot be deployed: ...", which
            // satisfied the old substring and made four cells pass on an unrelated
            // refusal (#321). Keep the phrase stable, or fix the test with it.
            errors.Add(
                $"{noun} start event '{LabelOf(start)}' cannot {where}. {remedy} Left where it " +
                "is, Flowable refuses the whole deployment, not just this step.");
        }

        // #309. Flowable also refuses a plain subprocess with MORE THAN ONE start
        // event (`flowable-subprocess-multiple-start-event`), and that is the same
        // class of whole-deployment failure.
        foreach (var subProcess in document.Descendants(BpmnNamespace + "subProcess"))
        {
            // An event subprocess may legitimately carry several start events --
            // one per trigger it reacts to -- so the rule is for plain ones only.
            if (string.Equals(
                    subProcess.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var starts = subProcess.Elements(BpmnNamespace + "startEvent").ToList();
            if (starts.Count <= 1) continue;

            errors.Add(
                $"Subprocess '{LabelOf(subProcess)}' has {starts.Count} start events. A subprocess " +
                "is entered once, by a token arriving on a sequence flow, so Flowable allows it " +
                "exactly one — and refuses the whole deployment otherwise. Keep one and join the " +
                "others to it with sequence flows.");
        }

        return errors;
    }

    // #163. An ad-hoc subprocess with no completion condition can never finish.
    //
    // Flowable deploys it happily and the instance then sits in the subprocess
    // forever, with the parent unable to continue — a hang, not a feature, which
    // is the line epic #40 draws.
    private static IReadOnlyList<string> BuildAdhocSubProcessErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var adhoc in document.Descendants(BpmnNamespace + "adHocSubProcess"))
        {
            var label = Trimmed(adhoc.Attribute("name")?.Value)
                        ?? Trimmed(adhoc.Attribute("id")?.Value)
                        ?? "Unnamed ad-hoc subprocess";

            var condition = Trimmed(
                adhoc.Attribute(ScriptTaskIdentity.AutoNateNamespace + CompletionConditionAttribute)?.Value)
                ?? Trimmed(adhoc.Element(BpmnNamespace + "completionCondition")?.Value);
            if (condition is null)
            {
                errors.Add(
                    $"The ad-hoc subprocess '{label}' has no completion condition. Without one it can " +
                    "never finish and the process stops there — set a condition that becomes true when " +
                    "the case is done.");
                continue;
            }

            // The same expression check every other condition in the diagram
            // gets. A completion condition that cannot parse is the same hang one
            // step further along — the subprocess simply never completes.
            var problem = WorkflowConditionValidation.DescribeSyntaxProblem(condition);
            if (problem is not null)
            {
                errors.Add($"The ad-hoc subprocess '{label}' has a completion condition that " +
                           $"cannot be evaluated: {problem}");
            }
        }

        return errors;
    }

    // #115. A compensation handler that WAITS is refused, because Flowable
    // 8.0.0 cannot run one.
    //
    // This started as a warning about ordering — with automatic handlers the
    // throw waits for compensation (recorded trail `h3;h1;after;`), with user
    // task handlers it does not. Probing further found something much worse.
    //
    // When compensation is triggered during the completion of a USER TASK and a
    // handler is itself a wait state, the engine fails the transaction outright:
    //
    //   ERROR: update or delete on table "act_ru_execution" violates foreign key
    //   constraint "act_fk_exe_parent"
    //
    // Reproduced against a bare Flowable with no Auton8 in the picture, and
    // isolated by elimination: removing the unreached activity's boundary event
    // still fails, removing the gateway still fails, and making the handlers
    // AUTOMATIC is the only change that fixes it. The task cannot be completed at
    // all — it stays open and the instance cannot move.
    //
    // Epic #40 draws the line here: a shape that leaves an instance unable to
    // complete is a defect to refuse, not a behaviour to document. Refusing at
    // publish turns an unrecoverable runtime crash into a sentence while the
    // author still has the diagram open.
    private static IReadOnlyList<string> BuildCompensationErrors(XDocument document)
    {
        var warnings = new List<string>();

        // Only associations whose SOURCE is a compensation boundary event. An
        // association is also how bpmn-js attaches a text annotation, so taking
        // every association's target treated an annotated user task as a
        // compensation handler and refused the whole diagram — publish, and (per
        // #234) save with it. Verification caught that; the fixture here now
        // covers a non-compensation association so it cannot come back.
        var compensationBoundaryIds = document
            .Descendants(BpmnNamespace + "boundaryEvent")
            .Where(boundary => boundary.Elements(BpmnNamespace + "compensateEventDefinition").Any())
            .Select(boundary => Trimmed(boundary.Attribute("id")?.Value))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal!);

        var handlerIds = document
            .Descendants(BpmnNamespace + "association")
            .Where(association =>
                compensationBoundaryIds.Contains(Trimmed(association.Attribute("sourceRef")?.Value) ?? string.Empty))
            .Select(association => Trimmed(association.Attribute("targetRef")?.Value))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal!);
        if (handlerIds.Count == 0) return warnings;

        foreach (var element in document.Descendants())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;

            var id = Trimmed(element.Attribute("id")?.Value);
            if (id is null || !handlerIds.Contains(id)) continue;

            // Elements that WAIT. An ordinary task, a service task or a script
            // task all complete within the compensation.
            //
            // A subProcess or callActivity is here because it can contain a user
            // task and therefore waits too — verification pointed out the
            // original pair let those through into the engine crash this refusal
            // exists to prevent.
            if (element.Name.LocalName is not
                ("userTask" or "receiveTask" or "subProcess" or "callActivity" or "adHocSubProcess"))
            {
                continue;
            }

            var label = Trimmed(element.Attribute("name")?.Value) ?? id;
            warnings.Add(
                $"The compensation handler '{label}' waits, and Flowable " +
                "cannot run one. When compensation is triggered while a user task is being " +
                "completed, a waiting handler fails the engine's own transaction and the task can " +
                "never be completed — the process stops there for good. Make the handler an " +
                "automatic step (a service or script task). If a person must confirm the undo, " +
                "have the handler start that work rather than be it.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildGatewayWarnings(XDocument document)
    {
        var warnings = new List<string>();

        var sequenceFlows = document.Descendants(BpmnNamespace + "sequenceFlow").ToList();
        var flowsBySource = sequenceFlows
            .GroupBy(f => f.Attribute("sourceRef")?.Value ?? string.Empty)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var inclusive in document.Descendants(BpmnNamespace + "inclusiveGateway"))
        {
            var gatewayId = inclusive.Attribute("id")?.Value ?? string.Empty;
            if (!flowsBySource.TryGetValue(gatewayId, out var outgoing) || outgoing.Count == 0)
            {
                continue;
            }

            var hasDefault = !string.IsNullOrWhiteSpace(inclusive.Attribute("default")?.Value);
            var hasAnyCondition = outgoing.Any(f => f.Element(BpmnNamespace + "conditionExpression") is not null);

            if (!hasDefault && !hasAnyCondition)
            {
                warnings.Add($"Inclusive gateway '{GatewayLabel(inclusive)}' has no conditions on its outgoing flows and no default flow. All outgoing paths will fire at runtime.");
            }
        }

        foreach (var parallel in document.Descendants(BpmnNamespace + "parallelGateway"))
        {
            var gatewayId = parallel.Attribute("id")?.Value ?? string.Empty;
            if (!flowsBySource.TryGetValue(gatewayId, out var outgoing) || outgoing.Count == 0)
            {
                continue;
            }

            if (outgoing.Any(f => f.Element(BpmnNamespace + "conditionExpression") is not null))
            {
                warnings.Add($"Parallel gateway '{GatewayLabel(parallel)}' has condition expressions on outgoing flows. Flowable ignores conditions on parallel-gateway outflows; remove them to clarify intent.");
            }
        }

        // Default-mode user tasks that feed an exclusive gateway expose one
        // button per outgoing flow in the runtime modal — buttons need a label.
        // Surface unnamed flows so the author can fix them before users see
        // a button captioned with a raw flow ID.
        foreach (var userTask in document.Descendants(BpmnNamespace + "userTask"))
        {
            if (!IsDefaultBehaviorUserTask(userTask))
            {
                continue;
            }

            var taskId = userTask.Attribute("id")?.Value ?? string.Empty;
            if (!flowsBySource.TryGetValue(taskId, out var taskOutflows) || taskOutflows.Count != 1)
            {
                continue;
            }

            var targetRef = taskOutflows[0].Attribute("targetRef")?.Value;
            if (string.IsNullOrEmpty(targetRef))
            {
                continue;
            }

            var target = document
                .Descendants(BpmnNamespace + "exclusiveGateway")
                .FirstOrDefault(e => e.Attribute("id")?.Value == targetRef);
            if (target is null)
            {
                continue;
            }

            if (!flowsBySource.TryGetValue(targetRef, out var gatewayFlows))
            {
                continue;
            }

            var unnamed = gatewayFlows
                .Where(f => string.IsNullOrWhiteSpace(f.Attribute("name")?.Value))
                .Select(f => f.Attribute("id")?.Value ?? "(no id)")
                .ToArray();
            if (unnamed.Length > 0)
            {
                var taskLabel = userTask.Attribute("name")?.Value
                    ?? userTask.Attribute("id")?.Value
                    ?? "(unnamed)";
                warnings.Add(
                    $"User task '{taskLabel}' feeds an exclusive gateway whose outgoing flow(s) have no name: {string.Join(", ", unnamed)}. The default-behavior modal will caption these buttons with the flow id.");
            }
        }

        return warnings;
    }

    /// <summary>
    /// A business rule task must name a decision table (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refused at prepare, where the author is, rather than at deployment, where
    /// the engine answers a 500 naming a Java class. The AC asks for exactly this:
    /// <i>"Configuring a business rule task without a table, or with one that has
    /// been deleted, fails at deployment with a clear message rather than at
    /// execution."</i>
    /// </para>
    /// <para>
    /// <b>Whether the table EXISTS is not checked here</b>, deliberately. This
    /// function is pure and sees only the XML; a deleted table is a database
    /// question, and answering it from here would mean either a DB round trip in a
    /// pure validator or a check that silently passes when it cannot look. The
    /// existence check belongs with the DB-aware rules
    /// (<c>BuildRecordTypeShortCodeWarningsAsync</c> is the precedent) and is
    /// wired at the publish endpoint.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildBusinessRuleTaskErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var task in document.Descendants(BpmnNamespace + "businessRuleTask"))
        {
            var key = task.Attribute(ScriptTaskIdentity.AutoNateNamespace + "decisionKey")?.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(
                    $"Business rule task '{ElementLabel(task)}' does not name a decision table. "
                    + "Open it and choose one — a business rule task with no table deploys as a step "
                    + "that decides nothing.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Warns where "interrupting" will not interrupt (#229).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An interrupting error event subprocess cancels the scope it guards — except
    /// when it sits in the same subprocess as the error end event that throws.
    /// Then the handler runs and a parallel sibling in that subprocess <b>keeps
    /// going</b>, while <c>isInterrupting="true"</c> sits on the diagram saying
    /// otherwise.
    /// </para>
    /// <para>
    /// **This is a warning, not a refusal.** Nothing is broken: the handler runs,
    /// the diagram deploys, and an author who wants exactly this can have it. What
    /// they cannot currently have is to know they have it, because on a canvas the
    /// difference from the interrupting shape is which box the handler is drawn
    /// in. A refusal would also break diagrams that are already deployed and
    /// working.
    /// </para>
    /// <para>
    /// **The condition is measured, not reasoned.**
    /// <c>EventSubProcessScopeDifferentialTests</c> runs all three arrangements
    /// against a live engine: a throw one scope deeper cancels the sibling, and so
    /// does a throw at the <em>process</em> level — it is specifically a handler
    /// inside a <c>subProcess</c>, catching an error thrown in that same
    /// subprocess, that does not. Keying the warning on "same scope" alone would
    /// have fired it on the process-level shape, which behaves correctly.
    /// </para>
    /// <para>
    /// Deliberately silent about what the specification requires. That question is
    /// open on #229; this describes the engine, which is what an author gets
    /// either way.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildSameScopeErrorHandlerWarnings(XDocument document)
    {
        var warnings = new List<string>();

        foreach (var scope in document.Descendants(BpmnNamespace + "subProcess"))
        {
            // The scope itself must not be an event subprocess: a handler is not
            // the scope whose siblings are at stake.
            if (string.Equals(scope.Attribute("triggeredByEvent")?.Value, "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var handlers = scope.Elements(BpmnNamespace + "subProcess")
                .Where(child => string.Equals(
                    child.Attribute("triggeredByEvent")?.Value, "true",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (handlers.Count == 0) continue;

            // An error end event that is a DIRECT child of this scope. One nested
            // deeper is the arrangement that interrupts correctly, so Descendants
            // would make the warning fire on the working shape.
            var throwsHere = scope.Elements(BpmnNamespace + "endEvent")
                .Any(end => end.Element(BpmnNamespace + "errorEventDefinition") is not null);
            if (!throwsHere) continue;

            foreach (var handler in handlers)
            {
                var interruptingErrorStart = handler.Elements(BpmnNamespace + "startEvent")
                    .Any(start =>
                        start.Element(BpmnNamespace + "errorEventDefinition") is not null
                        && !string.Equals(start.Attribute("isInterrupting")?.Value, "false",
                            StringComparison.OrdinalIgnoreCase));
                if (!interruptingErrorStart) continue;

                warnings.Add(
                    $"Event subprocess '{ElementLabel(handler)}' catches an error thrown in its own "
                    + $"subprocess '{ElementLabel(scope)}'. It is marked interrupting, but in this "
                    + "arrangement Flowable runs the handler WITHOUT cancelling the other work in "
                    + $"'{ElementLabel(scope)}' — parallel branches there keep running. Move the throwing "
                    + "step into a nested subprocess if you need the scope cancelled.");
            }
        }

        return warnings;
    }

    /// <summary>A human label for any element: its name, else its id.</summary>
    private static string ElementLabel(XElement element)
    {
        var name = element.Attribute("name")?.Value;
        return string.IsNullOrWhiteSpace(name)
            ? element.Attribute("id")?.Value ?? "(unnamed)"
            : name;
    }

    private static string GatewayLabel(XElement gateway)
    {
        var name = gateway.Attribute("name")?.Value;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return gateway.Attribute("id")?.Value ?? "(unnamed)";
    }
}

public sealed record class WorkflowBpmnValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static WorkflowBpmnValidationResult WithError(string error)
    {
        return new WorkflowBpmnValidationResult([error], []);
    }
}
