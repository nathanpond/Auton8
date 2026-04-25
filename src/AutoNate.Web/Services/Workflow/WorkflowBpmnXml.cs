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
        "serviceTask",
        "businessRuleTask",
        "sendTask",
        "receiveTask",
        "manualTask"
    ];
    private static readonly HashSet<string> UnsupportedRuntimeControlElementNames =
    [
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

            var scriptTaskErrors = BuildScriptTaskValidationErrors(document);
            if (scriptTaskErrors.Count > 0)
            {
                return new WorkflowBpmnValidationResult(
                    scriptTaskErrors,
                    BuildUnsupportedRuntimeWarnings(document));
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
        }
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
