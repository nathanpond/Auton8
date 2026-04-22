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
        Assert.Contains(result.Warnings, warning => warning.Contains("exclusive gateways", StringComparison.OrdinalIgnoreCase));
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
