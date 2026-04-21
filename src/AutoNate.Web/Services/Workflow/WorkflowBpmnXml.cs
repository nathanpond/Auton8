using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AutoNate.Web.Services.Workflow;

public static partial class WorkflowBpmnXml
{
    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace BpmndiNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";
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
        "task",
        "serviceTask",
        "scriptTask",
        "businessRuleTask",
        "sendTask",
        "receiveTask",
        "manualTask"
    ];
    private static readonly HashSet<string> UnsupportedRuntimeControlElementNames =
    [
        "exclusiveGateway",
        "inclusiveGateway",
        "parallelGateway",
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
                                   id="Definitions_{{normalizedProcessKey}}"
                                   targetNamespace="http://autonate.dev/workflows">
                 <bpmn:process id="{{normalizedProcessKey}}" name="{{SecurityElement.Escape(normalizedWorkflowName)}}" isExecutable="true">
                   <bpmn:startEvent id="StartEvent_1" name="Start">
                     <bpmn:outgoing>Flow_1</bpmn:outgoing>
                   </bpmn:startEvent>
                   <bpmn:userTask id="Activity_InitialReview" name="Initial Review">
                     <bpmn:incoming>Flow_1</bpmn:incoming>
                     <bpmn:outgoing>Flow_2</bpmn:outgoing>
                   </bpmn:userTask>
                   <bpmn:userTask id="Activity_ManagerReview" name="Manager Review">
                     <bpmn:incoming>Flow_2</bpmn:incoming>
                     <bpmn:outgoing>Flow_3</bpmn:outgoing>
                   </bpmn:userTask>
                   <bpmn:userTask id="Activity_FinalApproval" name="Final Approval">
                     <bpmn:incoming>Flow_3</bpmn:incoming>
                     <bpmn:outgoing>Flow_4</bpmn:outgoing>
                   </bpmn:userTask>
                   <bpmn:endEvent id="Event_End" name="Done">
                     <bpmn:incoming>Flow_4</bpmn:incoming>
                   </bpmn:endEvent>
                   <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="Activity_InitialReview" />
                   <bpmn:sequenceFlow id="Flow_2" sourceRef="Activity_InitialReview" targetRef="Activity_ManagerReview" />
                   <bpmn:sequenceFlow id="Flow_3" sourceRef="Activity_ManagerReview" targetRef="Activity_FinalApproval" />
                   <bpmn:sequenceFlow id="Flow_4" sourceRef="Activity_FinalApproval" targetRef="Event_End" />
                 </bpmn:process>
                 <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                   <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="{{normalizedProcessKey}}">
                     <bpmndi:BPMNShape id="Shape_StartEvent_1" bpmnElement="StartEvent_1">
                       <dc:Bounds x="180" y="140" width="36" height="36" />
                     </bpmndi:BPMNShape>
                     <bpmndi:BPMNShape id="Shape_Activity_InitialReview" bpmnElement="Activity_InitialReview">
                       <dc:Bounds x="290" y="118" width="120" height="80" />
                     </bpmndi:BPMNShape>
                     <bpmndi:BPMNShape id="Shape_Activity_ManagerReview" bpmnElement="Activity_ManagerReview">
                       <dc:Bounds x="470" y="118" width="120" height="80" />
                     </bpmndi:BPMNShape>
                     <bpmndi:BPMNShape id="Shape_Activity_FinalApproval" bpmnElement="Activity_FinalApproval">
                       <dc:Bounds x="650" y="118" width="120" height="80" />
                     </bpmndi:BPMNShape>
                     <bpmndi:BPMNShape id="Shape_Event_End" bpmnElement="Event_End">
                       <dc:Bounds x="850" y="140" width="36" height="36" />
                     </bpmndi:BPMNShape>
                     <bpmndi:BPMNEdge id="Edge_Flow_1" bpmnElement="Flow_1">
                       <di:waypoint x="216" y="158" />
                       <di:waypoint x="290" y="158" />
                     </bpmndi:BPMNEdge>
                     <bpmndi:BPMNEdge id="Edge_Flow_2" bpmnElement="Flow_2">
                       <di:waypoint x="410" y="158" />
                       <di:waypoint x="470" y="158" />
                     </bpmndi:BPMNEdge>
                     <bpmndi:BPMNEdge id="Edge_Flow_3" bpmnElement="Flow_3">
                       <di:waypoint x="590" y="158" />
                       <di:waypoint x="650" y="158" />
                     </bpmndi:BPMNEdge>
                     <bpmndi:BPMNEdge id="Edge_Flow_4" bpmnElement="Flow_4">
                       <di:waypoint x="770" y="158" />
                       <di:waypoint x="850" y="158" />
                     </bpmndi:BPMNEdge>
                   </bpmndi:BPMNPlane>
                 </bpmndi:BPMNDiagram>
               </bpmn:definitions>
               """;
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

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
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

            return new WorkflowBpmnValidationResult(
                [],
                BuildUnsupportedRuntimeWarnings(document));
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
        }
    }

    private static string? ToBpmnLocalName(string? bpmnType)
    {
        if (string.IsNullOrWhiteSpace(bpmnType))
        {
            return null;
        }

        var separatorIndex = bpmnType.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex >= 0
            ? bpmnType[(separatorIndex + 1)..]
            : bpmnType;
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

                controlElements.Add(ToFriendlyElementName(localName));
            }

            if (UnsupportedRuntimeCollaborationElementNames.Contains(localName))
            {
                collaborationElements.Add(ToFriendlyElementName(localName));
            }

            if (localName.EndsWith("EventDefinition", StringComparison.Ordinal) &&
                !localName.Equals("TerminateEventDefinition", StringComparison.Ordinal))
            {
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

    private static string ToFriendlyElementName(string localName)
    {
        return localName switch
        {
            "task" => "generic tasks",
            "serviceTask" => "service tasks",
            "scriptTask" => "script tasks",
            "businessRuleTask" => "business rule tasks",
            "sendTask" => "send tasks",
            "receiveTask" => "receive tasks",
            "manualTask" => "manual tasks",
            "exclusiveGateway" => "exclusive gateways",
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
            "MessageEventDefinition" => "message events",
            "TimerEventDefinition" => "timer events",
            "ConditionalEventDefinition" => "conditional events",
            "SignalEventDefinition" => "signal events",
            "EscalationEventDefinition" => "escalation events",
            "ErrorEventDefinition" => "error events",
            "CancelEventDefinition" => "cancel events",
            "CompensateEventDefinition" => "compensation events",
            "LinkEventDefinition" => "link events",
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
