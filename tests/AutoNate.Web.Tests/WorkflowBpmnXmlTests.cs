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
                               <bpmn:serviceTask id="ServiceTask_1" name="Automate" />
                               <bpmn:exclusiveGateway id="Gateway_1" />
                               <bpmn:subProcess id="SubProcess_1" triggeredByEvent="true" />
                               <bpmn:participant id="Participant_1" processRef="warning_flow" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var result = WorkflowBpmnXml.ValidateProcess(xml);

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, warning => warning.Contains("service tasks", StringComparison.OrdinalIgnoreCase));
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

}
