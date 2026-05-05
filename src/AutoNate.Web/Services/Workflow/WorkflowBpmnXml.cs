using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AutoNate.Web.Services.Workflow;

public static partial class WorkflowBpmnXml
{
    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace BpmndiNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace FlowableNamespace = "http://flowable.org/bpmn";

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
    private static readonly HashSet<string> UnsupportedRuntimeTaskElementNames =
    [
        "businessRuleTask",
        "sendTask",
        "receiveTask",
        "manualTask"
    ];
    private static readonly HashSet<string> UnsupportedRuntimeControlElementNames =
    [
        "eventBasedGateway",
        "complexGateway",
        "boundaryEvent",
        "callActivity",
        "subProcess",
        "transaction",
        "adHocSubProcess",
        "intermediateCatchEvent",
        "intermediateThrowEvent"
    ];
    private static readonly HashSet<string> UnsupportedRuntimeCollaborationElementNames =
    [
        "collaboration",
        "participant",
        "lane",
        "messageFlow"
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

    public static WorkflowBpmnValidationResult ValidateProcess(string xml)
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
            errors.AddRange(BuildSignalStartEventValidationErrors(document));
            errors.AddRange(BuildTimerStartEventValidationErrors(document));
            errors.AddRange(BuildTimerIntermediateCatchEventValidationErrors(document));
            errors.AddRange(BuildServiceTaskValidationErrors(document));

            var warnings = new List<string>();
            warnings.AddRange(BuildUnsupportedRuntimeWarnings(document));
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
        return string.IsNullOrWhiteSpace(trimmed) ? "AutoNate Workflow" : trimmed;
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
        return $"AutoNate Workflow {DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
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
            signalEventDefinition.SetAttributeValue("signalRef", null);
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
    public static IReadOnlyList<WorkflowSignalRegistration> ExtractSignalRegistrations(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<WorkflowSignalRegistration>();
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return Array.Empty<WorkflowSignalRegistration>();
        }

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

        return set;
    }

    private static readonly IReadOnlySet<string> EmptyShortCodeSet =
        new HashSet<string>(StringComparer.Ordinal);

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
        return value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal);
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
            if (!string.Equals(scriptFormat, "javascript", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Script task '{taskLabel}' must use scriptFormat=\"javascript\".");
            }

            var scriptBody = scriptTask.Element(BpmnNamespace + "script")?.Value;
            if (string.IsNullOrWhiteSpace(scriptBody))
            {
                errors.Add($"Script task '{taskLabel}' must include a non-empty inline script body.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> BuildUnsupportedRuntimeWarnings(XDocument document)
    {
        var taskElements = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var controlElements = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var collaborationElements = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventDrivenBehaviors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in document.Descendants())
        {
            if (element.Name.Namespace != BpmnNamespace)
            {
                continue;
            }

            var localName = element.Name.LocalName;

            if (UnsupportedRuntimeTaskElementNames.Contains(localName))
            {
                taskElements.Add(ToFriendlyElementName(localName));
            }

            if (UnsupportedRuntimeControlElementNames.Contains(localName))
            {
                if (localName.Equals("subProcess", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    eventDrivenBehaviors.Add("event subprocesses");
                    continue;
                }

                // Timer intermediate catch events are first-class — only warn for
                // the message/signal/conditional flavors that aren't wired up yet.
                if (localName.Equals("intermediateCatchEvent", StringComparison.Ordinal) &&
                    element.Element(BpmnNamespace + "timerEventDefinition") is not null)
                {
                    continue;
                }

                controlElements.Add(ToFriendlyElementName(localName));
            }

            if (UnsupportedRuntimeCollaborationElementNames.Contains(localName))
            {
                collaborationElements.Add(ToFriendlyElementName(localName));
            }

            if (localName.EndsWith("EventDefinition", StringComparison.Ordinal) &&
                !localName.Equals("terminateEventDefinition", StringComparison.Ordinal))
            {
                // Signal and timer start events are now first-class — only warn
                // for event definitions that are NOT on a start event (boundary,
                // intermediate, end events still trigger the warning).
                if ((localName.Equals("signalEventDefinition", StringComparison.Ordinal) ||
                     localName.Equals("timerEventDefinition", StringComparison.Ordinal)) &&
                    element.Parent?.Name == BpmnNamespace + "startEvent")
                {
                    continue;
                }

                if (localName.Equals("timerEventDefinition", StringComparison.Ordinal) &&
                    element.Parent?.Name == BpmnNamespace + "intermediateCatchEvent")
                {
                    continue;
                }

                eventDrivenBehaviors.Add(ToFriendlyElementName(localName));
            }
        }

        var warnings = new List<string>();

        if (taskElements.Count > 0)
        {
            warnings.Add($"This BPMN is valid BPMN and may deploy to Flowable, but AutoNate does not fully support these non-user task elements in Workflow Studio/runtime yet: {string.Join(", ", taskElements)}.");
        }

        if (controlElements.Count > 0)
        {
            warnings.Add($"This BPMN is valid BPMN and may deploy to Flowable, but AutoNate does not fully support these orchestration/control constructs in Workflow Studio/runtime yet: {string.Join(", ", controlElements)}.");
        }

        if (eventDrivenBehaviors.Count > 0)
        {
            warnings.Add($"This BPMN is valid BPMN and may deploy to Flowable, but AutoNate does not fully support these event-driven behaviors in Workflow Studio/runtime yet: {string.Join(", ", eventDrivenBehaviors)}.");
        }

        if (collaborationElements.Count > 0)
        {
            warnings.Add($"This BPMN is valid BPMN and may deploy to Flowable, but AutoNate does not fully support these pool/lane/collaboration constructs in Workflow Studio/runtime yet: {string.Join(", ", collaborationElements)}.");
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

    private static string ToFriendlyElementName(string localName)
    {
        return localName switch
        {
            "serviceTask" => "service tasks",
            "scriptTask" => "script tasks",
            "businessRuleTask" => "business rule tasks",
            "sendTask" => "send tasks",
            "receiveTask" => "receive tasks",
            "manualTask" => "manual tasks",
            "inclusiveGateway" => "inclusive gateways",
            "parallelGateway" => "parallel gateways",
            "eventBasedGateway" => "event-based gateways",
            "complexGateway" => "complex gateways",
            "boundaryEvent" => "boundary events",
            "callActivity" => "call activities",
            "subProcess" => "sub-processes",
            "transaction" => "transactions",
            "adHocSubProcess" => "ad-hoc sub-processes",
            "intermediateCatchEvent" => "intermediate catch events",
            "intermediateThrowEvent" => "intermediate throw events",
            "collaboration" => "collaborations",
            "participant" => "participants",
            "lane" => "lanes",
            "messageFlow" => "message flows",
            "messageEventDefinition" => "message events",
            "timerEventDefinition" => "timer events",
            "conditionalEventDefinition" => "conditional events",
            "signalEventDefinition" => "signal events",
            "escalationEventDefinition" => "escalation events",
            "errorEventDefinition" => "error events",
            "cancelEventDefinition" => "cancel events",
            "compensateEventDefinition" => "compensation events",
            "linkEventDefinition" => "link events",
            _ => localName
        };
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
