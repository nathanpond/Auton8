using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The studio draws an event subprocess as BPMN says it should (#162).
/// </summary>
/// <remarks>
/// bpmn-js renders this natively — dotted border for the event subprocess, and a
/// dashed circle for a non-interrupting start event against a solid one for
/// interrupting. That it *should* is not evidence that it *does* in this build,
/// which is the whole reason this story asks. So the rendered SVG is read.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class EventSubProcessStudioTests : E2ETestBase
{
    public EventSubProcessStudioTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_event_subprocess_is_dotted_and_its_start_events_show_whether_they_interrupt()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var name = TestNames.Prefixed("event-subprocess-render");

        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = Guid.NewGuid(),
                name,
                processKey = "event_subprocess_render",
                bpmnXml = Diagram
            }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();

        var handler = page.Locator("[data-element-id='Handler_1']");
        await Assertions.Expect(handler).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The event subprocess is drawn dotted, which is what tells an author at a
        // glance that it is triggered rather than entered.
        //
        // Read from the `style` attribute, not a `stroke-dasharray` attribute:
        // bpmn-js sets it inline as CSS, and querying the attribute returns null
        // for a shape that is in fact dotted. Getting that wrong the first time
        // produced a confident "this is drawn solid" about a border that was
        // already correct.
        var handlerStyle = await handler.Locator("rect").First.GetAttributeAsync("style");
        Assert.Contains("stroke-dasharray", handlerStyle ?? string.Empty);

        // Interrupting and non-interrupting start events must be distinguishable.
        // BPMN draws the non-interrupting one dashed; asserting they DIFFER says
        // the distinction is visible without pinning the exact dash pattern.
        var interrupting = await page.Locator("[data-element-id='Interrupting_1'] circle").First
            .GetAttributeAsync("style");
        var nonInterrupting = await page.Locator("[data-element-id='NonInterrupting_1'] circle").First
            .GetAttributeAsync("style");

        Assert.NotEqual(interrupting ?? string.Empty, nonInterrupting ?? string.Empty);

        // And specifically: the non-interrupting one is the dashed of the two.
        Assert.Contains("stroke-dasharray", nonInterrupting ?? string.Empty);
    }

    private const string Diagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" />
          <bpmn:escalation id="Esc_1" escalationCode="ESC1" />
          <bpmn:process id="event_subprocess_render" name="Render" isExecutable="true">
            <bpmn:startEvent id="Start_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Work_1" />
            <bpmn:userTask id="Work_1" name="Work" />
            <bpmn:subProcess id="Handler_1" name="On error" triggeredByEvent="true">
              <bpmn:startEvent id="Interrupting_1" isInterrupting="true">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:startEvent>
            </bpmn:subProcess>
            <bpmn:subProcess id="Handler_2" name="On escalation" triggeredByEvent="true">
              <bpmn:startEvent id="NonInterrupting_1" isInterrupting="false">
                <bpmn:escalationEventDefinition escalationRef="Esc_1" />
              </bpmn:startEvent>
            </bpmn:subProcess>
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="event_subprocess_render">
              <bpmndi:BPMNShape id="S_Start" bpmnElement="Start_1">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_Work" bpmnElement="Work_1">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_H1" bpmnElement="Handler_1" isExpanded="true">
                <dc:Bounds x="100" y="220" width="220" height="120" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_I1" bpmnElement="Interrupting_1">
                <dc:Bounds x="130" y="260" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_H2" bpmnElement="Handler_2" isExpanded="true">
                <dc:Bounds x="360" y="220" width="220" height="120" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="S_N1" bpmnElement="NonInterrupting_1">
                <dc:Bounds x="390" y="260" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
