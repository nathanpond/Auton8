using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The studio's BPMN property editors, and the hook every spec that drives them
/// depends on (#78, E2E-036/E2E-037).
/// </summary>
/// <remarks>
/// <para>
/// <b>UNTRAITED, deliberately, and that is the story's own instruction.</b> These
/// journeys open a diagram, edit it and reload it; none of them publishes, so none
/// of them needs Flowable. <c>WorkflowStudioTests</c> carries
/// <c>RequiresService=Flowable</c> because publishing there fails without it, and
/// the trait is per CLASS -- so putting canvas coverage in that file would push it
/// into a tier no pull request runs. The half of E2E-036 that genuinely needs a
/// running engine (publish, start, open the resulting task surface) stays there.
/// </para>
/// <para>
/// <c>WorkflowPaletteTests</c> and <c>WorkflowBpmnSupportPanelTests</c> already
/// drive this canvas untraited, so this is the established boundary rather than a
/// new claim about what the studio needs.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowStudioEditorTests : E2ETestBase
{
    public WorkflowStudioEditorTests(AutoNateE2EFixture fixture) : base(fixture) { }

    /// <summary>
    /// Every rendered element carries its authored id (#78).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing pinned this, and eight spec files are built on it.</b>
    /// <c>data-element-id</c> is stock diagram-js: it is not a test-only
    /// affordance, it ships to real users, and no custom renderer strips it today.
    /// That is exactly why it is worth a guard -- it is inert, so nobody would
    /// think to check it, and a future custom renderer would take out
    /// <c>BusinessRuleTaskStudioTests</c>, <c>CallActivityStudioTests</c>,
    /// <c>ComplexGatewayStudioRoundTripTests</c> and five others in one commit,
    /// with failures that look like the editors breaking rather than the hook.
    /// </para>
    /// <para>
    /// Asserted on a SHAPE and a CONNECTION, because diagram-js stamps both and a
    /// renderer replacing only one would leave half the specs working. And on the
    /// AUTHORED id specifically -- an attribute carrying some internal handle
    /// would satisfy "the attribute exists" while every selector in those eight
    /// files still missed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_rendered_element_carries_the_id_the_author_gave_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await OpenSeededDiagramAsync(page, "elementid", EditorDiagram);

        // A shape.
        await Assertions.Expect(page.Locator("[data-element-id='approve']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // A CONNECTION. diagram-js stamps these too, and a renderer that handled
        // only shapes would leave every sequence-flow selector silently empty.
        await Assertions.Expect(page.Locator("[data-element-id='f1']"))
            .ToHaveCountAsync(1);

        // Every authored id is present, not merely the two asserted above. A
        // renderer that stamped the first element and stopped would pass a
        // spot-check.
        foreach (var id in new[] { "start", "approve", "notify", "gate", "f1", "f2" })
        {
            await Assertions.Expect(page.Locator($"[data-element-id='{id}']"))
                .ToHaveCountAsync(1);
        }

        // THE COMPLEMENT: the attribute carries the AUTHORED id, not an internal
        // handle that merely happens to be unique. Without this, a renderer
        // stamping its own ids would satisfy every assertion above while breaking
        // all eight spec files that select by the id they seeded.
        var stamped = await page.Locator("[data-element-id]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-element-id'))");

        Assert.Contains("approve", stamped);
        Assert.Contains("f1", stamped);
    }

    private static async Task OpenSeededDiagramAsync(IPage page, string slug, string xml)
    {
        var id = Guid.NewGuid();
        var name = TestNames.Prefixed($"studio-{slug}");
        var created = await page.APIRequest.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = $"studio_{slug}", bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Seeding failed: {created.Status} {await created.TextAsync()}");

        await page.GotoAsync("/workflow");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workflow Studio" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var selector = page.GetByRole(AriaRole.Combobox, new() { Name = "Workflow Model" });
        await Assertions.Expect(selector).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await selector.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();
    }

    private const string EditorDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="studio_elementid" name="Editors" isExecutable="true">
            <bpmn:startEvent id="start" />
            <bpmn:sequenceFlow id="f1" sourceRef="start" targetRef="approve" />
            <bpmn:userTask id="approve" name="Approve" />
            <bpmn:sequenceFlow id="f2" sourceRef="approve" targetRef="gate" />
            <bpmn:exclusiveGateway id="gate" name="Decide" />
            <bpmn:serviceTask id="notify" name="Notify" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="studio_elementid">
              <bpmndi:BPMNShape id="Shape_start" bpmnElement="start">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_approve" bpmnElement="approve">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_gate" bpmnElement="gate">
                <dc:Bounds x="360" y="95" width="50" height="50" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_notify" bpmnElement="notify">
                <dc:Bounds x="200" y="220" width="100" height="80" />
              </bpmndi:BPMNShape>
              <!-- The EDGES need DI too. Measured: without a BPMNEdge bpmn-js
                   renders no connection at all, so `[data-element-id='f1']`
                   resolved to zero elements while every shape was present. A
                   guard for the connection half would have been vacuous on a
                   diagram that draws no connections. -->
              <bpmndi:BPMNEdge id="Edge_f1" bpmnElement="f1">
                <di:waypoint x="136" y="118" />
                <di:waypoint x="200" y="120" />
              </bpmndi:BPMNEdge>
              <bpmndi:BPMNEdge id="Edge_f2" bpmnElement="f2">
                <di:waypoint x="300" y="120" />
                <di:waypoint x="360" y="120" />
              </bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
