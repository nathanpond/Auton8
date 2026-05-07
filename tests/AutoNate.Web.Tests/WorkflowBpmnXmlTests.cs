using AutoNate.Web.Services.Workflow;
using System.Xml.Linq;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class WorkflowBpmnXmlTests
{
    [Fact]
    public void ValidateProcess_ReturnsWarnings_ForUnsupportedRuntimeConstructs()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="warning_flow" name="Warning Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:businessRuleTask id="BusinessRuleTask_1" name="Decide" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:subProcess id="SubProcess_1" triggeredByEvent="true" />
                               <bpmn:participant id="Participant_1" processRef="warning_flow" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, warning => warning.Contains("business rule tasks", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("service tasks", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("exclusive gateways", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("event subprocesses", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("participants", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenProcessIsMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenScriptTaskUsesNonJavaScriptFormat()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="groovy">
                                 <bpmn:script>execution.setVariable("value", 1);</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, error => error.Contains("scriptFormat=\"javascript\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsJavaScriptScriptTask()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="javascript">
                                 <bpmn:script>execution.setVariable("value", 1);</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("script tasks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenScriptTaskBodyIsMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="javascript" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, error => error.Contains("inline script body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesScriptTaskFieldsFromElementSnapshots()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
                               <bpmn:userTask id="Task_1" name="Compute" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "script_flow",
            "Script Flow",
            [
                new WorkflowElementSnapshot(
                    "Task_1",
                    "bpmn:ScriptTask",
                    "Compute",
                    "javascript",
                    "execution.setVariable(\"total\", 42);",
                    "total")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var task = document.Descendants(bpmn + "scriptTask").Single();

        Assert.Equal("javascript", task.Attribute("scriptFormat")?.Value);
        Assert.Equal("total", task.Attribute("resultVariable")?.Value);
        Assert.Equal("execution.setVariable(\"total\", 42);", task.Element(bpmn + "script")?.Value);
    }

    // Script tasks must serialize as flowable:async="true" so a thrown error
    // in the script becomes a job.execution.failed event instead of synchronously
    // 500ing the start-process API call.
    [Fact]
    public void ApplyProcessMetadata_ForcesScriptTasksAsync()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="async_script_flow" name="Async Script Flow" isExecutable="true">
                               <bpmn:scriptTask id="ScriptTask_1" name="Boom" scriptFormat="javascript">
                                 <bpmn:script>throw new Error("boom");</bpmn:script>
                               </bpmn:scriptTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(xml, "async_script_flow", "Async Script Flow");

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";
        var task = document.Descendants(bpmn + "scriptTask").Single();

        Assert.Equal("true", task.Attribute(flowable + "async")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_EmitsFlowableUserTaskAssignmentAttributes()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="assign_flow" name="Assign Flow" isExecutable="true">
                               <bpmn:userTask id="Task_Literal" name="Review" />
                               <bpmn:userTask id="Task_Expr" name="Approve" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "assign_flow",
            "Assign Flow",
            [
                new WorkflowElementSnapshot(
                    "Task_Literal",
                    "bpmn:UserTask",
                    "Review",
                    Assignee: "11111111-1111-1111-1111-111111111111",
                    CandidateUsers: ["aaaa", "bbbb"],
                    CandidateGroups: ["reviewers", "approvers"]),
                new WorkflowElementSnapshot(
                    "Task_Expr",
                    "bpmn:UserTask",
                    "Approve",
                    Assignee: "${initiator}",
                    CandidateUsers: ["${currentRecord.assignees}"],
                    CandidateGroups: [])
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        Assert.Equal("http://flowable.org/bpmn", document.Root!.GetNamespaceOfPrefix("flowable")?.NamespaceName);

        var literalTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Literal");
        Assert.Equal("11111111-1111-1111-1111-111111111111", literalTask.Attribute(flowable + "assignee")?.Value);
        Assert.Equal("aaaa,bbbb", literalTask.Attribute(flowable + "candidateUsers")?.Value);
        Assert.Equal("reviewers,approvers", literalTask.Attribute(flowable + "candidateGroups")?.Value);

        var expressionTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Expr");
        Assert.Equal("${initiator}", expressionTask.Attribute(flowable + "assignee")?.Value);
        Assert.Equal("${currentRecord.assignees}", expressionTask.Attribute(flowable + "candidateUsers")?.Value);
        Assert.Null(expressionTask.Attribute(flowable + "candidateGroups"));
    }

    [Fact]
    public void ApplyProcessMetadata_RoundTripsFlowableUserTaskDueDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="due_flow" name="Due Flow" isExecutable="true">
                               <bpmn:userTask id="Task_Activation" name="Review" />
                               <bpmn:userTask id="Task_FromStart" name="Approve" />
                               <bpmn:userTask id="Task_Cleared" name="Cleanup" flowable:dueDate="P7D" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "due_flow",
            "Due Flow",
            [
                new WorkflowElementSnapshot(
                    "Task_Activation",
                    "bpmn:UserTask",
                    "Review",
                    DueDate: "P3D"),
                new WorkflowElementSnapshot(
                    "Task_FromStart",
                    "bpmn:UserTask",
                    "Approve",
                    DueDate: "${dueDateHelper.fromProcessStart(execution, 5)}"),
                new WorkflowElementSnapshot(
                    "Task_Cleared",
                    "bpmn:UserTask",
                    "Cleanup",
                    DueDate: null)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var activationTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Activation");
        Assert.Equal("P3D", activationTask.Attribute(flowable + "dueDate")?.Value);

        var fromStartTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_FromStart");
        Assert.Equal("${dueDateHelper.fromProcessStart(execution, 5)}", fromStartTask.Attribute(flowable + "dueDate")?.Value);

        var clearedTask = document.Descendants(bpmn + "userTask").Single(t => t.Attribute("id")!.Value == "Task_Cleared");
        Assert.Null(clearedTask.Attribute(flowable + "dueDate"));
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesSequenceFlowConditionExpressionFromElementSnapshots()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                             xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                             xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="decision_flow" name="Decision Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="EndEvent_1" />
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="Gateway_1" targetRef="EndEvent_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "decision_flow",
            "Decision Flow",
            [
                new WorkflowElementSnapshot(
                    "Flow_1",
                    "bpmn:SequenceFlow",
                    "Approved path",
                    ConditionExpression: "${needsApproval}")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var flow = document.Descendants(bpmn + "sequenceFlow").Single();
        var expression = flow.Element(bpmn + "conditionExpression");

        Assert.Equal("Approved path", flow.Attribute("name")?.Value);
        Assert.NotNull(expression);
        Assert.Equal("bpmn:tFormalExpression", expression!.Attribute(xsi + "type")?.Value);
        Assert.Equal("${needsApproval}", expression.Value);
    }

    [Fact]
    public void CreateStarterDiagram_CreatesBlankProcessWithoutSeedTasks()
    {
        var xml = WorkflowBpmnXml.CreateStarterDiagram("test_process", "Test Workflow");
        var document = XDocument.Parse(xml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace bpmndi = "http://www.omg.org/spec/BPMN/20100524/DI";

        var process = document.Descendants(bpmn + "process").Single();
        var plane = document.Descendants(bpmndi + "BPMNPlane").Single();

        Assert.Equal("test_process", process.Attribute("id")?.Value);
        Assert.Equal("Test Workflow", process.Attribute("name")?.Value);
        Assert.Empty(process.Elements());
        Assert.Empty(plane.Elements());
    }

    // --- Signal start events --------------------------------------------------

    [Fact]
    public void ApplyProcessMetadata_MaterializesSignalRoot_FromStartEventSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "signal_flow",
            "Signal Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    SignalName: "OrderPlaced",
                    SignalTopic: "orders.events")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var signal = Assert.Single(document.Root!.Elements(bpmn + "signal"));
        Assert.Equal("OrderPlaced", signal.Attribute("name")?.Value);
        Assert.Equal("orders.events", signal.Attribute(flowable + "topic")?.Value);

        var startEvent = document.Descendants(bpmn + "startEvent").Single();
        var signalEventDefinition = startEvent.Element(bpmn + "signalEventDefinition")!;
        Assert.Equal(signal.Attribute("id")?.Value, signalEventDefinition.Attribute("signalRef")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_DefaultsTopicToWorkflowSignals_WhenSnapshotTopicMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "signal_flow",
            "Signal Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    SignalName: "OrderPlaced")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var signal = Assert.Single(document.Root!.Elements(bpmn + "signal"));
        Assert.Equal(WorkflowBpmnXml.DefaultSignalTopic, signal.Attribute(flowable + "topic")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_PrunesOrphanSignalRoots_WhenSnapshotClearsName()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" flowable:topic="orders.events" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "signal_flow",
            "Signal Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    SignalName: null)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        Assert.Empty(document.Root!.Elements(bpmn + "signal"));
        var signalEventDefinition = document.Descendants(bpmn + "signalEventDefinition").Single();
        Assert.Null(signalEventDefinition.Attribute("signalRef"));
    }

    [Fact]
    public void ApplySignalStartEventSnapshot_WritesAndReadsRecordTypeShortCodes()
    {
        const string initial = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:flowable="http://flowable.org/bpmn"
                                                 id="Definitions_1"
                                                 targetNamespace="http://autonate.dev/workflows">
                                 <bpmn:signal id="Signal_Record" name="record.created" flowable:topic="record.events" />
                                 <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                                   <bpmn:startEvent id="SE">
                                     <bpmn:signalEventDefinition signalRef="Signal_Record" />
                                   </bpmn:startEvent>
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        var snapshot = new WorkflowElementSnapshot(
            Id: "SE",
            Type: "bpmn:StartEvent",
            Name: null,
            SignalName: "record.created",
            SignalTopic: "record.events",
            RecordTypeShortCodes: new[] { "asset", "vehicle" });

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            initial,
            "OrderFlow",
            "Order Flow",
            [snapshot]);

        Assert.Contains("flowable:recordTypeShortCodes=\"asset,vehicle\"", updated);

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(updated);
        var registration = Assert.Single(registrations);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "asset", "vehicle" },
            registration.RecordTypeShortCodes);
    }

    [Fact]
    public void ApplySignalStartEventSnapshot_OmitsAttribute_WhenFilterEmpty()
    {
        // Initial XML already carries the attribute — applying an empty filter
        // must clear it so the workflow reverts to "match all records".
        const string initial = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:flowable="http://flowable.org/bpmn"
                                                 id="Definitions_1"
                                                 targetNamespace="http://autonate.dev/workflows">
                                 <bpmn:signal id="Signal_Record" name="record.created" flowable:topic="record.events" />
                                 <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                                   <bpmn:startEvent id="SE">
                                     <bpmn:signalEventDefinition signalRef="Signal_Record" flowable:recordTypeShortCodes="asset" />
                                   </bpmn:startEvent>
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        var snapshot = new WorkflowElementSnapshot(
            Id: "SE",
            Type: "bpmn:StartEvent",
            Name: null,
            SignalName: "record.created",
            SignalTopic: "record.events",
            RecordTypeShortCodes: Array.Empty<string>());

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            initial,
            "OrderFlow",
            "Order Flow",
            [snapshot]);

        Assert.DoesNotContain("flowable:recordTypeShortCodes", updated);
    }

    [Fact]
    public void ExtractSignalRegistrations_ReturnsTuplesForEverySignalStartEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" flowable:topic="orders.events" />
                             <bpmn:signal id="Signal_Stock" name="StockChanged" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:startEvent>
                               <bpmn:startEvent id="StartEvent_2">
                                 <bpmn:signalEventDefinition signalRef="Signal_Stock" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

        Assert.Equal(2, registrations.Count);
        Assert.Contains(registrations, r => r.SignalName == "OrderPlaced" && r.Topic == "orders.events");
        Assert.Contains(registrations, r =>
            r.SignalName == "StockChanged" && r.Topic == WorkflowBpmnXml.DefaultSignalTopic);
    }

    [Fact]
    public void ExtractSignalRegistrations_IgnoresSignalEventDefinitionsOnNonStartEvents()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:intermediateCatchEvent id="Intermediate_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

        Assert.Empty(registrations);
    }

    [Fact]
    public void ExtractSignalRegistrations_ReturnsEmpty_OnMalformedXml()
    {
        Assert.Empty(WorkflowBpmnXml.ExtractSignalRegistrations("not really xml"));
        Assert.Empty(WorkflowBpmnXml.ExtractSignalRegistrations(string.Empty));
    }

    [Fact]
    public void ExtractSignalRegistrations_PopulatesProcessDefinitionKey()
    {
        var xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:flowable="http://flowable.org/bpmn">
              <signal id="Signal_1" name="record.created" flowable:topic="record.events"/>
              <process id="OrderFlow">
                <startEvent id="StartEvent_1">
                  <signalEventDefinition signalRef="Signal_1"/>
                </startEvent>
              </process>
            </definitions>
            """;

        var registrations = WorkflowBpmnXml.ExtractSignalRegistrations(xml);

        var registration = Assert.Single(registrations);
        Assert.Equal("record.created", registration.SignalName);
        Assert.Equal("record.events", registration.Topic);
        Assert.Equal("OrderFlow", registration.ProcessDefinitionKey);
        Assert.Empty(registration.RecordTypeShortCodes);
    }

    [Fact]
    public void ValidateProcess_DoesNotWarnAboutSignalEventDefinition_OnStartEvents()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_Order" name="OrderPlaced" flowable:topic="orders.events" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_Order" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("signal events", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("signalEventDefinition", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsSignalStartEventWithoutEventType()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("Event Type", StringComparison.Ordinal));
    }

    // --- Timer start events ---------------------------------------------------

    [Fact]
    public void ApplyProcessMetadata_WritesCronTimeCycle_FromTimerStartSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 ? * MON-FRI")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        var timeCycle = timerEventDefinition.Element(bpmn + "timeCycle");
        Assert.NotNull(timeCycle);
        Assert.Equal("0 0 9 ? * MON-FRI", timeCycle!.Value);
        Assert.Equal("cron", timeCycle.Attribute(flowable + "type")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDate"));
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDuration"));
    }

    [Fact]
    public void ApplyProcessMetadata_WritesEndDateAttribute_WhenTimerSnapshotProvidesIt()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?",
                    TimerEndDate: "2026-12-31")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("2026-12-31", timerEventDefinition.Attribute(flowable + "endDate")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_RemovesEndDate_WhenTimerSnapshotClearsIt()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition flowable:endDate="2025-12-31">
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?",
                    TimerEndDate: null)
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Null(timerEventDefinition.Attribute(flowable + "endDate"));
        Assert.Empty(timerEventDefinition.Elements(flowable + "endDate"));
    }

    [Fact]
    public void ApplyProcessMetadata_NormalizesEndDateChildElement_ToAttributeForRoundTrip()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <flowable:endDate>2026-06-01</flowable:endDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?",
                    TimerEndDate: "2026-06-01")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("2026-06-01", timerEventDefinition.Attribute(flowable + "endDate")?.Value);
        Assert.Empty(timerEventDefinition.Elements(flowable + "endDate"));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerStartEventWithoutSchedule()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("recurrence schedule", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerStartEventWithMalformedCron()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeCycle flowable:type="cron">not a cron expression!!</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid cron expression", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerStartEventWithMalformedEndDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition flowable:endDate="not-a-date">
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid end date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsTimerStartEventWithValidCronAndEndDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition flowable:endDate="2026-12-31">
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 ? * MON-FRI</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_DoesNotWarnAboutTimerEventDefinition_OnStartEvents()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("timer events", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_RejectsStartEventWithBothTimerAndSignalDefinitions()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_X" name="OrderPlaced" />
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeCycle flowable:type="cron">0 0 9 * * ?</bpmn:timeCycle>
                                 </bpmn:timerEventDefinition>
                                 <bpmn:signalEventDefinition signalRef="Signal_X" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("signalEventDefinition", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyProcessMetadata_HandlesMultipleTimerStartEventsIndependently()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                               <bpmn:startEvent id="StartEvent_2">
                                 <bpmn:timerEventDefinition />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "StartEvent_1",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 9 * * ?"),
                new WorkflowElementSnapshot(
                    "StartEvent_2",
                    "bpmn:StartEvent",
                    null,
                    TimerCycleCron: "0 0 17 ? * FRI",
                    TimerEndDate: "2026-12-31")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var startEvents = document.Descendants(bpmn + "startEvent").ToArray();
        Assert.Equal(2, startEvents.Length);

        var first = startEvents.Single(e => e.Attribute("id")?.Value == "StartEvent_1");
        Assert.Equal("0 0 9 * * ?", first.Element(bpmn + "timerEventDefinition")?.Element(bpmn + "timeCycle")?.Value);
        Assert.Null(first.Element(bpmn + "timerEventDefinition")?.Attribute(flowable + "endDate"));

        var second = startEvents.Single(e => e.Attribute("id")?.Value == "StartEvent_2");
        Assert.Equal("0 0 17 ? * FRI", second.Element(bpmn + "timerEventDefinition")?.Element(bpmn + "timeCycle")?.Value);
        Assert.Equal("2026-12-31", second.Element(bpmn + "timerEventDefinition")?.Attribute(flowable + "endDate")?.Value);
    }

    [Fact]
    public void ValidateProcess_AllowsSignalStartEventListeningOnTelemetryTopic()
    {
        // Listening on the BusWatcher's own topic is now allowed: a workflow can
        // legitimately want to react to events Flowable itself publishes (e.g.
        // run a janitor workflow whenever any process completes). Loop avoidance
        // is the user's responsibility — pick a signal name that doesn't collide
        // with one of your own published events.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_X" name="process.completed" flowable:topic="workflow.execution.events" />
                             <bpmn:process id="signal_flow" name="Signal Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_X" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    // --- Timer intermediate catch events --------------------------------------

    [Fact]
    public void ApplyProcessMetadata_WritesTimeDuration_FromTimerIntermediateSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDuration: "PT15M")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("PT15M", timerEventDefinition.Element(bpmn + "timeDuration")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDate"));
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeCycle"));
    }

    [Fact]
    public void ApplyProcessMetadata_WritesTimeDate_FromTimerIntermediateSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDate: "2026-12-31T09:00:00")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("2026-12-31T09:00:00", timerEventDefinition.Element(bpmn + "timeDate")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDuration"));
    }

    [Fact]
    public void ApplyProcessMetadata_WritesExpression_FromTimerIntermediateDurationSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDuration: "${execution.getVariable('waitDuration')}")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var duration = document.Descendants(bpmn + "timerEventDefinition").Single()
            .Element(bpmn + "timeDuration")?.Value;
        Assert.Equal("${execution.getVariable('waitDuration')}", duration);
    }

    [Fact]
    public void ApplyProcessMetadata_SwitchingMode_DropsPreviousTimerKindOnIntermediateCatch()
    {
        // Previous shape: timeDate. New snapshot: timeDuration. Flowable
        // rejects multiple kinds, so the writer must clear the date child
        // when only duration is present.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDate>2026-01-01T09:00:00</bpmn:timeDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "timer_flow",
            "Timer Flow",
            [
                new WorkflowElementSnapshot(
                    "Catch_1",
                    "bpmn:IntermediateCatchEvent",
                    null,
                    TimerDuration: "PT30M")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

        var timerEventDefinition = document.Descendants(bpmn + "timerEventDefinition").Single();
        Assert.Equal("PT30M", timerEventDefinition.Element(bpmn + "timeDuration")?.Value);
        Assert.Empty(timerEventDefinition.Elements(bpmn + "timeDate"));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerIntermediateCatchEventWithoutSchedule()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("duration or date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerIntermediateCatchEventWithMalformedDuration()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDuration>15 minutes</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid duration", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_RejectsTimerIntermediateCatchEventWithMalformedDate()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDate>tomorrow at noon</bpmn:timeDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("invalid date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsTimerIntermediateCatchEventWithLiteralDuration()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDuration>P1DT12H</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_AcceptsTimerIntermediateCatchEventWithExpression()
    {
        // Expressions are evaluated at runtime by Flowable; the validator
        // can't sanity-check them, so we accept any ${...} body.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDate>${execution.getVariable('reminderDate')}</bpmn:timeDate>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_DoesNotWarnAboutTimerIntermediateCatchEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="timer_flow" name="Timer Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:timerEventDefinition>
                                   <bpmn:timeDuration>PT15M</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.DoesNotContain(
            result.Warnings,
            w => w.Contains("intermediate catch events", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Warnings,
            w => w.Contains("timer events", StringComparison.OrdinalIgnoreCase));
    }

    // --- Service tasks (behaviors) -------------------------------------------

    [Fact]
    public void ApplyProcessMetadata_WiresAutonateBehaviorDelegate_ForServiceTaskSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1" name="Unlock account" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Unlock account",
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("${autonateBehaviorDelegate}", serviceTask.Attribute(flowable + "delegateExpression")?.Value);
        Assert.Equal("true", serviceTask.Attribute(flowable + "exclusive")?.Value);
        Assert.Equal("behavior", serviceTask.Attribute(flowable + "autonateServiceKind")?.Value);
        Assert.Equal("autonate.unlock-account", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_StripsLegacyClassAttribute_OnServiceTaskSnapshot()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 name="Whatever"
                                                 flowable:class="com.example.LegacyDelegate"
                                                 flowable:expression="${ignored}" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    "Whatever",
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Null(serviceTask.Attribute(flowable + "class"));
        Assert.Null(serviceTask.Attribute(flowable + "expression"));
        Assert.Equal("${autonateBehaviorDelegate}", serviceTask.Attribute(flowable + "delegateExpression")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_OverwritesPreviousBehaviorKey_OnServiceTaskSnapshot()
    {
        // Previously wired to an older behavior via the attribute shape; re-
        // applying should leave a single behaviorKey attribute pointing at
        // the new value.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="behavior"
                                                 flowable:behaviorKey="old.behavior" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    null,
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("autonate.unlock-account", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_StripsLegacyFieldInjectionChildren_OnServiceTaskSnapshot()
    {
        // Earlier studio builds wrote behaviorKey/autonateServiceKind as
        // <flowable:field> child elements. Re-saving an older model should
        // promote them to attributes and leave no stale field children.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}">
                                 <bpmn:extensionElements>
                                   <flowable:field name="autonateServiceKind">
                                     <flowable:string>behavior</flowable:string>
                                   </flowable:field>
                                   <flowable:field name="behaviorKey">
                                     <flowable:string>old.behavior</flowable:string>
                                   </flowable:field>
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "behavior_flow",
            "Behavior Flow",
            [
                new WorkflowElementSnapshot(
                    "ServiceTask_1",
                    "bpmn:ServiceTask",
                    null,
                    BehaviorKey: "autonate.unlock-account")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace flowable = "http://flowable.org/bpmn";

        var serviceTask = document.Descendants(bpmn + "serviceTask").Single();
        Assert.Equal("autonate.unlock-account", serviceTask.Attribute(flowable + "behaviorKey")?.Value);
        // No leftover <flowable:field> children for the swept names.
        Assert.Empty(serviceTask
            .Element(bpmn + "extensionElements")
            ?.Elements(flowable + "field")
            .Where(f =>
            {
                var n = f.Attribute("name")?.Value;
                return n == "autonateServiceKind" || n == "behaviorKey";
            })
            ?? Enumerable.Empty<XElement>());
    }

    [Fact]
    public void ValidateProcess_RejectsServiceTask_WhenBehaviorKeyMissing()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="behavior" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("behavior selected", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_AcceptsServiceTask_WhenBehaviorConfigured()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="behavior_flow"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="behavior"
                                                 flowable:behaviorKey="autonate.unlock-account" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("service tasks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_AcceptsServiceTask_WrittenInLegacyFieldInjectionShape()
    {
        // Back-compat: workflows saved by the older studio build still
        // validate without the user having to re-save them first.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="behavior_flow"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}">
                                 <bpmn:extensionElements>
                                   <flowable:field name="autonateServiceKind">
                                     <flowable:string>behavior</flowable:string>
                                   </flowable:field>
                                   <flowable:field name="behaviorKey">
                                     <flowable:string>autonate.unlock-account</flowable:string>
                                   </flowable:field>
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_RejectsServiceTask_WithUnsupportedKind()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:delegateExpression="${autonateBehaviorDelegate}"
                                                 flowable:autonateServiceKind="http-call" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors, e => e.Contains("unsupported autonateServiceKind", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_IgnoresServiceTaskBoundToCustomDelegate()
    {
        // A service task wired to a non-AutoNate delegate (legitimate v2
        // shape: plugin ships its own JavaDelegate class) is left alone —
        // we don't validate behavior keys for it.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="behavior_flow" name="Behavior Flow" isExecutable="true">
                               <bpmn:serviceTask id="ServiceTask_1"
                                                 flowable:class="com.acme.MyDelegate" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateProcess_StillWarnsAboutNonTimerIntermediateCatchEvent()
    {
        // A signal/message intermediate catch event is still unsupported —
        // make sure we didn't accidentally whitelist the entire element type.
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="Signal_X" name="OrderPlaced" />
                             <bpmn:process id="catch_flow" name="Catch Flow" isExecutable="true">
                               <bpmn:intermediateCatchEvent id="Catch_1">
                                 <bpmn:signalEventDefinition signalRef="Signal_X" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(
            result.Warnings,
            w => w.Contains("intermediate catch events", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProcess_DoesNotWarn_ForInclusiveGatewayWithConditions()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" name="Branches" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A">
                                 <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${pathA}</bpmn:conditionExpression>
                               </bpmn:sequenceFlow>
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B">
                                 <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${pathB}</bpmn:conditionExpression>
                               </bpmn:sequenceFlow>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("inclusive gateways", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Inclusive gateway 'Branches'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_DoesNotWarn_ForParallelGateway()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="parallel_flow" name="Parallel Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:parallelGateway id="Gateway_1" name="Fork" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("parallel gateways", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Parallel gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_Warns_ForInclusiveGatewayWithoutConditionsOrDefault()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" name="Branches" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("Inclusive gateway 'Branches'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("no conditions on its outgoing flows and no default flow", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_DoesNotWarn_ForInclusiveGatewayWithDefaultFlow()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" name="Branches" default="Flow_A" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Inclusive gateway", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProcess_Warns_ForParallelGatewayWithConditionedOutflow()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="parallel_flow" name="Parallel Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:parallelGateway id="Gateway_1" name="Fork" />
                               <bpmn:endEvent id="EndEvent_A" />
                               <bpmn:endEvent id="EndEvent_B" />
                               <bpmn:sequenceFlow id="Flow_Start" sourceRef="StartEvent_1" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" sourceRef="Gateway_1" targetRef="EndEvent_A">
                                 <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${shouldGo}</bpmn:conditionExpression>
                               </bpmn:sequenceFlow>
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="EndEvent_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("Parallel gateway 'Fork'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("ignores conditions on parallel-gateway outflows", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesConditionExpression_OnInclusiveGatewayOutflow()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="inclusive_flow" name="Inclusive Flow" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:inclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="EndEvent_1" />
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="Gateway_1" targetRef="EndEvent_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updatedXml = WorkflowBpmnXml.ApplyProcessMetadata(
            xml,
            "inclusive_flow",
            "Inclusive Flow",
            [
                new WorkflowElementSnapshot(
                    "Flow_1",
                    "bpmn:SequenceFlow",
                    "High risk",
                    ConditionExpression: "${riskLevel == 'high'}")
            ]);

        var document = XDocument.Parse(updatedXml);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var flow = document.Descendants(bpmn + "sequenceFlow").Single();
        var expression = flow.Element(bpmn + "conditionExpression");

        Assert.Equal("High risk", flow.Attribute("name")?.Value);
        Assert.NotNull(expression);
        Assert.Equal("bpmn:tFormalExpression", expression!.Attribute(xsi + "type")?.Value);
        Assert.Equal("${riskLevel == 'high'}", expression.Value);
    }

    [Fact]
    public void ValidateProcess_ReturnsError_WhenRecordTypeFilterAppearsOnIntermediateCatch()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:signal id="S" name="record.created" flowable:topic="record.events" />
                             <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                               <bpmn:startEvent id="Start" />
                               <bpmn:intermediateCatchEvent id="Catch">
                                 <bpmn:signalEventDefinition signalRef="S" flowable:recordTypeShortCodes="asset" />
                               </bpmn:intermediateCatchEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Errors,
            e => e.Contains("recordTypeShortCodes", StringComparison.OrdinalIgnoreCase)
              && e.Contains("startEvent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplySignalStartEventSnapshot_ClearsRecordTypeShortCodes_WhenSignalNameCleared()
    {
        const string initial = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                 xmlns:flowable="http://flowable.org/bpmn"
                                                 id="Definitions_1"
                                                 targetNamespace="http://autonate.dev/workflows">
                                 <bpmn:signal id="S" name="record.created" flowable:topic="record.events" />
                                 <bpmn:process id="OrderFlow" name="Order Flow" isExecutable="true">
                                   <bpmn:startEvent id="SE">
                                     <bpmn:signalEventDefinition signalRef="S" flowable:recordTypeShortCodes="asset" />
                                   </bpmn:startEvent>
                                 </bpmn:process>
                               </bpmn:definitions>
                               """;

        var snapshot = new WorkflowElementSnapshot(
            Id: "SE",
            Type: "bpmn:StartEvent",
            Name: null,
            SignalName: null,                              // user cleared the signal name
            SignalTopic: "record.events",
            RecordTypeShortCodes: new[] { "asset" });      // stale; should be cleared with the name

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            initial,
            "OrderFlow",
            "Order Flow",
            [snapshot]);

        Assert.DoesNotContain("flowable:recordTypeShortCodes", updated);
        Assert.DoesNotContain("signalRef", updated); // pre-existing behavior — sanity check
    }

    // ----- Default-behavior user task → exclusive gateway auto-rewrite -----

    private const string GatewayChoiceFlowVariable = WorkflowBpmnXml.GatewayChoiceVariableName;

    private static string DefaultModeUserTaskBeforeGatewayXml(
        string? userTaskMode = null,
        bool firstFlowConditioned = false) =>
        $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                            xmlns:flowable="http://flowable.org/bpmn"
                            id="Definitions_1"
                            targetNamespace="http://autonate.dev/workflows">
            <bpmn:process id="approval_flow" name="Approval" isExecutable="true">
              <bpmn:startEvent id="StartEvent_1" />
              <bpmn:userTask id="Task_Approve" name="Approve"{{(userTaskMode is null ? string.Empty : $" flowable:userFormMode=\"{userTaskMode}\"")}}>
                <bpmn:documentation>Please review and pick a path.</bpmn:documentation>
              </bpmn:userTask>
              <bpmn:exclusiveGateway id="Gateway_1" />
              <bpmn:endEvent id="End_Approved" />
              <bpmn:endEvent id="End_Rejected" />
              <bpmn:sequenceFlow id="Flow_StartToTask" sourceRef="StartEvent_1" targetRef="Task_Approve" />
              <bpmn:sequenceFlow id="Flow_TaskToGateway" sourceRef="Task_Approve" targetRef="Gateway_1" />
              <bpmn:sequenceFlow id="Flow_Approve" name="Approve" sourceRef="Gateway_1" targetRef="End_Approved">
                {{(firstFlowConditioned ? "<bpmn:conditionExpression xsi:type=\"bpmn:tFormalExpression\">${author == 'wrote-this'}</bpmn:conditionExpression>" : string.Empty)}}
              </bpmn:sequenceFlow>
              <bpmn:sequenceFlow id="Flow_Reject" name="Reject" sourceRef="Gateway_1" targetRef="End_Rejected" />
            </bpmn:process>
          </bpmn:definitions>
          """;

    [Fact]
    public void ApplyProcessMetadata_InjectsGatewayChoiceConditions_ForDefaultUserTaskBeforeExclusiveGateway()
    {
        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(),
            "approval_flow",
            "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var approveFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Approve");
        var rejectFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Reject");

        Assert.Equal(
            $"${{{GatewayChoiceFlowVariable} == 'Flow_Approve'}}",
            approveFlow.Element(bpmn + "conditionExpression")?.Value);
        Assert.Equal(
            $"${{{GatewayChoiceFlowVariable} == 'Flow_Reject'}}",
            rejectFlow.Element(bpmn + "conditionExpression")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_PreservesAuthorAuthoredConditions_OnGatewayFlows()
    {
        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(firstFlowConditioned: true),
            "approval_flow",
            "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var approveFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Approve");
        var rejectFlow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == "Flow_Reject");

        Assert.Equal(
            "${author == 'wrote-this'}",
            approveFlow.Element(bpmn + "conditionExpression")?.Value);
        Assert.Equal(
            $"${{{GatewayChoiceFlowVariable} == 'Flow_Reject'}}",
            rejectFlow.Element(bpmn + "conditionExpression")?.Value);
    }

    [Fact]
    public void ApplyProcessMetadata_IsIdempotent_ForGatewayChoiceConditions()
    {
        var first = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(),
            "approval_flow",
            "Approval");
        var second = WorkflowBpmnXml.ApplyProcessMetadata(first, "approval_flow", "Approval");

        var document = XDocument.Parse(second);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        foreach (var flowId in new[] { "Flow_Approve", "Flow_Reject" })
        {
            var flow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == flowId);
            Assert.Single(flow.Elements(bpmn + "conditionExpression"));
        }
    }

    [Fact]
    public void ApplyProcessMetadata_DoesNotInjectConditions_ForFormModeUserTask()
    {
        var updated = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(userTaskMode: "modal"),
            "approval_flow",
            "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        foreach (var flowId in new[] { "Flow_Approve", "Flow_Reject" })
        {
            var flow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == flowId);
            Assert.Null(flow.Element(bpmn + "conditionExpression"));
        }
    }

    [Fact]
    public void ApplyProcessMetadata_DoesNotInjectConditions_WhenGatewayIsInclusiveOrParallel()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="approval_flow" name="Approval" isExecutable="true">
                               <bpmn:userTask id="Task_Approve" name="Approve" />
                               <bpmn:inclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="End_A" />
                               <bpmn:endEvent id="End_B" />
                               <bpmn:sequenceFlow id="Flow_TaskToGateway" sourceRef="Task_Approve" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" name="A" sourceRef="Gateway_1" targetRef="End_A" />
                               <bpmn:sequenceFlow id="Flow_B" name="B" sourceRef="Gateway_1" targetRef="End_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var updated = WorkflowBpmnXml.ApplyProcessMetadata(xml, "approval_flow", "Approval");

        var document = XDocument.Parse(updated);
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        foreach (var flowId in new[] { "Flow_A", "Flow_B" })
        {
            var flow = document.Descendants(bpmn + "sequenceFlow").Single(f => f.Attribute("id")?.Value == flowId);
            Assert.Null(flow.Element(bpmn + "conditionExpression"));
        }
    }

    [Fact]
    public void TryDescribeGatewayChoices_ReturnsChoicesAndDescription_ForDefaultModeUserTaskBeforeGateway()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(),
            "approval_flow",
            "Approval");

        var description = WorkflowBpmnXml.TryDescribeGatewayChoices(prepared, "Task_Approve");

        Assert.NotNull(description);
        Assert.Equal("Please review and pick a path.", description!.Description);
        Assert.Equal(2, description.Choices.Count);
        Assert.Equal("Flow_Approve", description.Choices[0].FlowId);
        Assert.Equal("Approve", description.Choices[0].Label);
        Assert.Equal("Flow_Reject", description.Choices[1].FlowId);
        Assert.Equal("Reject", description.Choices[1].Label);
    }

    [Fact]
    public void TryDescribeGatewayChoices_ReturnsEmptyChoices_WhenTaskIsFormMode()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(
            DefaultModeUserTaskBeforeGatewayXml(userTaskMode: "modal"),
            "approval_flow",
            "Approval");

        var description = WorkflowBpmnXml.TryDescribeGatewayChoices(prepared, "Task_Approve");

        Assert.NotNull(description);
        Assert.Empty(description!.Choices);
    }

    [Fact]
    public void TryDescribeGatewayChoices_ReturnsEmptyChoices_WhenTaskIsNotBeforeAnExclusiveGateway()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="simple_flow" name="Simple" isExecutable="true">
                               <bpmn:userTask id="Task_1" name="Step" />
                               <bpmn:endEvent id="End_1" />
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="Task_1" targetRef="End_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var description = WorkflowBpmnXml.TryDescribeGatewayChoices(xml, "Task_1");

        Assert.NotNull(description);
        Assert.Empty(description!.Choices);
    }

    [Fact]
    public void ValidateProcess_WarnsAboutUnnamedGatewayFlows_UnderDefaultUserTask()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:flowable="http://flowable.org/bpmn"
                                             id="Definitions_1"
                                             targetNamespace="http://autonate.dev/workflows">
                             <bpmn:process id="approval_flow" name="Approval" isExecutable="true">
                               <bpmn:userTask id="Task_Approve" name="Approve" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:endEvent id="End_A" />
                               <bpmn:endEvent id="End_B" />
                               <bpmn:sequenceFlow id="Flow_TaskToGateway" sourceRef="Task_Approve" targetRef="Gateway_1" />
                               <bpmn:sequenceFlow id="Flow_A" name="Yes" sourceRef="Gateway_1" targetRef="End_A" />
                               <bpmn:sequenceFlow id="Flow_B" sourceRef="Gateway_1" targetRef="End_B" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Contains(result.Warnings, w => w.Contains("Flow_B", StringComparison.Ordinal));
    }
}
