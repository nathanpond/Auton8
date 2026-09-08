using System.Collections.Frozen;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
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
    public const string DefaultSignalTopic = "workflow.signals";
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
        var processElement = document.Descendants(BpmnNamespace + "process").FirstOrDefault()
            ?? throw new InvalidOperationException(BuildMissingProcessDefinitionMessage(document));

        EnsureFlowableNamespaceDeclared(document);
        PruneOrphanSignalRoots(document);

        var oldProcessKey = processElement.Attribute("id")?.Value;
        var normalizedProcessKey = NormalizeProcessKey(processKey);
        var normalizedWorkflowName = NormalizeWorkflowName(workflowName);

        processElement.SetAttributeValue("id", normalizedProcessKey);
        processElement.SetAttributeValue("name", normalizedWorkflowName);
        processElement.SetAttributeValue("isExecutable", "true");

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
        ExpandComplexGateways(document);
        ApplySignalScopes(document);

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
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

            process.Add(new XElement(BpmnNamespace + "endEvent", new XAttribute("id", terminalId)));
            process.Add(new XElement(BpmnNamespace + "sequenceFlow",
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

            // Rewire every flow INTO the gateway so it lands on the script task
            // instead, then flow the script task into the gateway.
            foreach (var inbound in document.Descendants(BpmnNamespace + "sequenceFlow")
                         .Where(flow => flow.Attribute("targetRef")?.Value == gatewayId))
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
                new XAttribute("scriptFormat", Trimmed(gateway.Attribute("scriptFormat")?.Value) ?? "javascript"),
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
            gateway.Attribute("scriptFormat")?.Remove();
            gateway.Attribute(ScriptTaskIdentity.AutoNateNamespace + ScriptTaskIdentity.RunAsAttribute)?.Remove();
            gateway.Attribute(ScriptTaskIdentity.RunAsAttribute)?.Remove();
            gateway.Element(BpmnNamespace + "script")?.Remove();

            gateway.AddBeforeSelf(scriptTask);
            gateway.Parent.Add(new XElement(
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

            // Conditions on the author's own outgoing flows. An author-written
            // condition is left alone, exactly as ApplyAutoNateGatewayConditions
            // does — the script chooses among the routes it was given, and an
            // author who has already written a condition meant it.
            foreach (var flow in outgoing)
            {
                var flowId = flow.Attribute("id")?.Value;
                if (string.IsNullOrWhiteSpace(flowId) || flowId == defaultFlowId) continue;
                if (flow.Element(BpmnNamespace + "conditionExpression") is not null) continue;

                flow.Add(new XElement(
                    BpmnNamespace + "conditionExpression",
                    new XAttribute(XsiNamespace + "type", "bpmn:tFormalExpression"),
                    $"${{{resultVariable} == '{flowId}'}}"));
            }
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

    // Marks a generated node as belonging to an author's element, so the
    // execution view can map Flowable's activity ids back onto the diagram the
    // author actually drew.
    internal const string ComplexGatewaySourceAttribute = "autonateExpandedFrom";

    // The routes the script may return, as a comma-separated list of flow ids.
    internal const string ComplexGatewayRoutesAttribute = "autonateAllowedRoutes";

    /// <summary>An author's routing script, stored on the gateway itself.</summary>
    private static string? ReadComplexGatewayScript(XElement gateway) =>
        gateway.Element(BpmnNamespace + "script")?.Value;

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
    private static string? ReadSignalScope(XElement element) =>
        element.Element(BpmnNamespace + "extensionElements")?
            .Elements()
            .FirstOrDefault(child => child.Name.LocalName == "autonateSignalScope")?
            .Attribute("value")?.Value;

    private static void ApplySignalScopes(XDocument document)
    {
        var root = document.Root;
        if (root is null) return;

        var signalsById = root.Elements(BpmnNamespace + "signal")
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Attribute("id")?.Value))
            .ToDictionary(signal => signal.Attribute("id")!.Value, signal => signal, StringComparer.Ordinal);
        if (signalsById.Count == 0) return;

        // One signal element per (name, scope) actually used. The FIRST scope seen
        // for a name reuses the original element; a second scope for the same name
        // gets its own, because two events that agree on a name but not on who
        // hears it are genuinely different subscriptions and cannot share one.
        var byNameAndScope = new Dictionary<(string Name, bool Scoped), XElement>();

        foreach (var element in document.Descendants().ToList())
        {
            if (element.Name.Namespace != BpmnNamespace) continue;

            var definition = element.Elements(BpmnNamespace + "signalEventDefinition").FirstOrDefault();
            var signalRef = definition?.Attribute("signalRef")?.Value;
            if (definition is null
                || string.IsNullOrWhiteSpace(signalRef)
                || !signalsById.TryGetValue(signalRef!, out var original))
            {
                continue;
            }

            var name = original.Attribute("name")?.Value ?? signalRef!;

            // Three states, not two. An event that says nothing is NOT the same as
            // one that says "global":
            //
            //   "instance" — scope the signal.
            //   "global"   — unscope it.
            //   absent     — leave the signal exactly as authored.
            //
            // The third case matters because a diagram may already carry
            // Flowable's own flowable:scope, written by hand or by another
            // modeller. Treating absent as "global" stripped it, silently widening
            // a signal its author had deliberately narrowed. That is how this was
            // found: a test wrote flowable:scope directly, publish removed it, and
            // the instance-scoped assertion failed only under load — in isolation
            // the check ran before the other instance had reacted, so it passed
            // for the wrong reason.
            var declared = ReadSignalScope(element);
            if (string.IsNullOrWhiteSpace(declared)) continue;

            var wantScoped = string.Equals(declared, "instance", StringComparison.OrdinalIgnoreCase);

            var key = (name, wantScoped);
            if (!byNameAndScope.TryGetValue(key, out var target))
            {
                var nameTaken = byNameAndScope.Keys.Any(k => k.Name == name);
                if (nameTaken)
                {
                    target = new XElement(original);
                    target.SetAttributeValue("id", $"{signalRef}_{(wantScoped ? "scoped" : "global")}");
                    original.AddAfterSelf(target);
                }
                else
                {
                    target = original;
                }

                target.SetAttributeValue(
                    FlowableNamespace + "scope", wantScoped ? "processInstance" : null);
                byNameAndScope[key] = target;
            }

            definition.SetAttributeValue("signalRef", target.Attribute("id")?.Value);
        }
    }

    // The execution diagram renders from the DEPLOYED definition, so an element
    // with no BPMNShape would be invisible there. Cloned from the element it
    // follows and nudged along, which is close enough to be legible and cannot
    // fail on a diagram that never had DI in the first place.
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
                    new XAttribute(XsiNamespace + "type", "bpmn:tFormalExpression"),
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
            var processElement = document.Descendants(BpmnNamespace + "process").FirstOrDefault();
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
            // #158: a conditional start event is legal only inside an event
            // subprocess. Flowable rejects it anywhere else with a parse error an
            // author cannot act on, so say what the constraint is instead.
            errors.AddRange(BuildConditionalStartPlacementErrors(document));
            // #157: a timer boundary with no time set never fires.
            errors.AddRange(BuildTimerBoundaryEventValidationErrors(document));
            // #161: a subprocess the engine cannot enter.
            errors.AddRange(BuildSubProcessValidationErrors(document));
            // #167: elements the studio converts away, and converted tasks nobody can do.
            errors.AddRange(BuildNonWaitingTaskErrors(document));
            errors.AddRange(BuildUncaughtThrownCodeErrors(document));
            // #164: a gateway that cannot be a choice, or points somewhere the
            // engine will not follow.
            errors.AddRange(BuildEventBasedGatewayErrors(document));
            // #162: an event subprocess that can never trigger.
            errors.AddRange(BuildEventSubProcessErrors(document));

            // #158: every condition in the diagram, through the one shared check.
            // Sequence flows included, so exclusive and inclusive gateways benefit
            // here rather than in a story of their own.
            var conditions = WorkflowConditionValidation.CheckDocument(document);
            errors.AddRange(conditions.Errors);

            var warnings = new List<string>();
            warnings.AddRange(conditions.Warnings);
            warnings.AddRange(BuildGatewayWarnings(document));

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

            if (string.Equals(element.Name.LocalName, "startEvent", StringComparison.Ordinal) &&
                element.Element(BpmnNamespace + "signalEventDefinition") is not null)
            {
                ApplySignalStartEventSnapshot(document, element, snapshot);
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
                condition.SetAttributeValue(XsiNamespace + "type", "bpmn:tFormalExpression");
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

            declarations.Add(new WorkflowMessageSendDeclaration(
                elementId,
                messageName.Trim(),
                Trimmed(element.Attribute(FlowableNamespace + TargetProcessKeyAttribute)?.Value),
                Trimmed(element.Attribute(FlowableNamespace + CorrelationKeyAttribute)?.Value),
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
    /// Every point in a published definition that can be advanced from outside,
    /// with the variable that addresses it (#112).
    /// </summary>
    public static IReadOnlyList<WorkflowMessageDeclaration> ExtractMessageDeclarations(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<WorkflowMessageDeclaration>();
        }

        var document = XDocument.Parse(xml);

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

        if (!string.IsNullOrWhiteSpace(snapshot.ResultVariable))
        {
            element.SetAttributeValue("resultVariable", snapshot.ResultVariable);
        }
        else
        {
            element.SetAttributeValue("resultVariable", null);
        }

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
            element.SetAttributeValue("scriptFormat", snapshot.ScriptFormat);
        }

        if (snapshot.Script is null) return;

        var scriptElement = element.Element(BpmnNamespace + "script");
        if (string.IsNullOrWhiteSpace(snapshot.Script))
        {
            scriptElement?.Remove();
            return;
        }

        if (scriptElement is null)
        {
            element.Add(new XElement(BpmnNamespace + "script", snapshot.Script));
            return;
        }

        scriptElement.Value = snapshot.Script;
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
        conditionExpressionElement.SetAttributeValue(XsiNamespace + "type", "bpmn:tFormalExpression");
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

            var scriptFormat = gateway.Attribute("scriptFormat")?.Value;
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
            var scriptBody = gateway.Element(BpmnNamespace + "script")?.Value;
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
                new XAttribute(XsiNamespace + "type", "bpmn:tFormalExpression"),
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

        return
        [
            .. BuildUncaughtThrownCodeErrors(document),
            .. BuildEventBasedGatewayErrors(document),
            // #162 — an event subprocess that can never trigger, or one promising
            // not to interrupt when the engine will interrupt anyway. Both deploy
            // cleanly and neither tells the author anything.
            .. BuildEventSubProcessErrors(document)
        ];
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
                .Select(code => code!)
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
                    else caught.Add(code);
                }
            }

            if (caught.Contains("*")) continue;

            foreach (var throwing in process.Descendants(BpmnNamespace + "endEvent")
                         .Where(e => e.Elements(BpmnNamespace + "errorEventDefinition").Any()))
            {
                var code = throwing.Elements(BpmnNamespace + "errorEventDefinition")
                    .Select(d => d.Attribute("errorRef")?.Value)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

                if (string.IsNullOrWhiteSpace(code) || caught.Contains(code!)) continue;

                errors.Add(
                    $"The error end event '{LabelOf(throwing)}' raises '{code}', and nothing in " +
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

    private static IReadOnlyList<string> BuildConditionalStartPlacementErrors(XDocument document)
    {
        var errors = new List<string>();

        foreach (var definition in document.Descendants(BpmnNamespace + "conditionalEventDefinition"))
        {
            var start = definition.Parent;
            if (start is null || start.Name != BpmnNamespace + "startEvent") continue;

            // An event subprocess is a subProcess carrying triggeredByEvent, which
            // is the one container where this start event is legal.
            var container = start.Parent;
            var inEventSubProcess = container is not null
                && container.Name == BpmnNamespace + "subProcess"
                && string.Equals(
                    container.Attribute("triggeredByEvent")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase);

            if (inEventSubProcess) continue;

            var label = start.Attribute("name")?.Value ?? start.Attribute("id")?.Value ?? "(unnamed)";
            errors.Add(
                $"Conditional start event '{label}' cannot start a process. BPMN allows a " +
                "conditional start event only inside an event subprocess, where it reacts to a " +
                "condition becoming true while the process is already running. To start a " +
                "process when a condition holds, start it another way and wait on an " +
                "intermediate catch conditional event instead.");
        }

        return errors;
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
